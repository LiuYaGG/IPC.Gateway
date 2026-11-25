using System.Security.Claims;
using System.Security.Cryptography;

namespace IPC.Gateway.WebHost;

/// <summary>Process-local, one-use approvals. Neither an API token nor another node can mint or consume them.</summary>
public sealed class GatewaySensitiveOperationService(TimeProvider time)
{
    public const string Header = "X-IPC-Reauth";
    private sealed record Scope(string UserId,string Session,string Stamp,string Method,string Target,string BodyHash,long Deadline);
    private readonly object _sync = new();
    private readonly Dictionary<string,Scope> _contexts = new(StringComparer.Ordinal);
    private readonly Dictionary<string,Scope> _tickets = new(StringComparer.Ordinal);
    private long Now => time.GetTimestamp();
    private long Deadline => Now + (long)(120 * time.TimestampFrequency);
    public static bool IsCookie(ClaimsPrincipal user) => user.Identity?.AuthenticationType == "GatewayCookie" &&
        Guid.TryParse(user.FindFirstValue("jti"),out _) && !string.IsNullOrEmpty(user.FindFirstValue("security_stamp"));
    private Scope Capture(ClaimsPrincipal user,string method,string target,string hash) => new(
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "",user.FindFirstValue("jti") ?? "",
        user.FindFirstValue("security_stamp") ?? "",method,target,hash,Deadline);
    private static bool MatchesUser(Scope scope,ClaimsPrincipal user) => IsCookie(user) &&
        scope.UserId == user.FindFirstValue(ClaimTypes.NameIdentifier) && scope.Session == user.FindFirstValue("jti") &&
        scope.Stamp == user.FindFirstValue("security_stamp");
    private void Cleanup()
    {
        long now=Now;
        foreach(var key in _contexts.Where(x=>x.Value.Deadline<=now).Select(x=>x.Key).ToArray())_contexts.Remove(key);
        foreach(var key in _tickets.Where(x=>x.Value.Deadline<=now).Select(x=>x.Key).ToArray())_tickets.Remove(key);
    }
    public string CreateContext(ClaimsPrincipal user,string method,string target,string bodyHash)
    {
        if(!IsCookie(user))throw new InvalidOperationException("请使用网页登录账号完成二次验证。");
        lock(_sync)
        {
            Cleanup();
            var existing = _contexts.FirstOrDefault(x=>MatchesUser(x.Value,user)&&x.Value.Method==method&&x.Value.Target==target&&x.Value.BodyHash==bodyHash);
            if(existing.Key!=null)return existing.Key;
            foreach(var stale in _contexts.Where(x=>x.Value.UserId==user.FindFirstValue(ClaimTypes.NameIdentifier)).OrderBy(x=>x.Value.Deadline).Take(Math.Max(0,_contexts.Count(x=>x.Value.UserId==user.FindFirstValue(ClaimTypes.NameIdentifier))-3)).Select(x=>x.Key).ToArray())_contexts.Remove(stale);
            if(_contexts.Count>=1024)throw new InvalidOperationException("安全验证繁忙，请稍后重试。");
            string id=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _contexts.Add(id,Capture(user,method,target,bodyHash));return id;
        }
    }
    public bool HasContext(string id,ClaimsPrincipal user)
    {
        lock(_sync){Cleanup();return _contexts.TryGetValue(id,out var scope)&&MatchesUser(scope,user);}
    }
    public string Issue(string id,ClaimsPrincipal user)
    {
        lock(_sync)
        {
            Cleanup();
            if(!_contexts.TryGetValue(id,out var scope)||!MatchesUser(scope,user))throw new InvalidOperationException("安全验证已失效，请重新执行操作。");
            if(_tickets.Count>=1024)throw new InvalidOperationException("安全验证繁忙，请稍后重试。");
            _contexts.Remove(id);
            string ticket=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _tickets.Add(ticket,scope with {Deadline=Deadline});return ticket;
        }
    }
    public bool Consume(string ticket,ClaimsPrincipal user,string method,string target,string bodyHash)
    {
        lock(_sync)
        {
            Cleanup();
            return _tickets.Remove(ticket,out var scope)&&MatchesUser(scope,user)&&scope.Method==method&&scope.Target==target&&scope.BodyHash==bodyHash;
        }
    }
}

public sealed class GatewaySensitiveOperationMiddleware(RequestDelegate next)
{
    public static bool RequiresVerification(string method,string path)
    {
        if(HttpMethods.IsGet(method)||HttpMethods.IsHead(method)||HttpMethods.IsOptions(method))return false;
        path=path.TrimEnd('/').ToLowerInvariant();
        return path=="/api/maintenance/updates/apply" ||
            path.StartsWith("/api/maintenance/updates/rollback/") && path.EndsWith("/prepare") ||
            path.StartsWith("/api/commercial/drivers/") && path.EndsWith("/signing") ||
            path=="/api/security/driver-trust" ||
            path=="/api/ha/disaster" ||
            path=="/api/ha/transport/prepare" || path=="/api/ha/transport/apply" ||
            path=="/api/ha/cluster-switch" ||
            path=="/api/auth/password" || path.StartsWith("/api/auth/users") || path.StartsWith("/api/auth/roles");
    }
    public async Task InvokeAsync(HttpContext context,GatewaySensitiveOperationService approvals)
    {
        var request=context.Request;
        if(!RequiresVerification(request.Method,request.Path.Value??"")){await next(context);return;}
        if(!GatewaySensitiveOperationService.IsCookie(context.User))
        {
            context.Response.StatusCode=403;
            await context.Response.WriteAsJsonAsync(new {success=false,errorCode="REAUTH_COOKIE_REQUIRED",errorMessage="此操作需要使用网页登录账号完成二次验证。"});
            return;
        }
        // The protected operations are small JSON commands, never package uploads.
        const int limit=1024*1024;
        if(request.ContentLength>limit){context.Response.StatusCode=413;return;}
        request.EnableBuffering(bufferThreshold:64*1024,bufferLimit:limit+1L);
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer=new byte[8192];int total=0;
        try
        {
            int length;
            while((length=await request.Body.ReadAsync(buffer,context.RequestAborted))!=0)
            {
                total+=length;
                if(total>limit){context.Response.StatusCode=413;return;}
                hash.AppendData(buffer,0,length);
            }
        }
        catch(IOException){context.Response.StatusCode=413;return;}
        finally{request.Body.Position=0;}
        string digest=Convert.ToHexString(hash.GetHashAndReset()), target=request.Path+request.QueryString;
        string supplied=request.Headers[GatewaySensitiveOperationService.Header].ToString();
        if(approvals.Consume(supplied,context.User,request.Method,target,digest)){await next(context);return;}
        string id;
        try{id=approvals.CreateContext(context.User,request.Method,target,digest);}
        catch(InvalidOperationException ex)
        {
            context.Response.StatusCode=429;await context.Response.WriteAsJsonAsync(new {success=false,errorMessage=ex.Message});return;
        }
        context.Response.StatusCode=403;
        context.Response.Headers.CacheControl="no-store";
        await context.Response.WriteAsJsonAsync(new {success=false,errorCode="REAUTH_REQUIRED",errorMessage="请验证当前密码后继续此高危操作。",reauthContext=id});
    }
}

public static class GatewaySensitiveAuthEndpoints
{
    public sealed record Request(string Password,string CaptchaToken,string ContextId);
    public static IEndpointRouteBuilder MapGatewaySensitiveAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/auth/reauth",async (HttpContext context,Request request,GatewayAuthService auth,
            GatewayLoginCaptchaService captcha,GatewaySensitiveOperationService approvals,IPC.Gateway.Core.Gateway.IGatewayAuditLogStore audit)=>
        {
            context.Response.Headers.CacheControl="no-store";
            // Explicitly validate the cookie, regardless of middleware allowlists or API-token identity.
            var validated=await auth.ValidateTokenAsync(GatewayAuthEndpoints.ReadToken(context.Request));
            if(!validated.Success||!GatewaySensitiveOperationService.IsCookie(validated.Principal))return Results.Unauthorized();
            var user=validated.Principal;
            if(!approvals.HasContext(request.ContextId??"",user))return Results.BadRequest(new {success=false,errorMessage="安全验证已失效，请重新执行操作。"});
            if(!captcha.TryConsumeForLogin(context.Connection.RemoteIpAddress,request.CaptchaToken,out string error))
                return Results.BadRequest(new {success=false,errorMessage=error});
            var login=await auth.LoginAsync(user.Identity!.Name!,request.Password??"");
            if(!login.Success)
            {
                await GatewayAuthEndpoints.WriteSecurityAuditAsync(context,audit,"auth.reauth","failed","sensitive-operation",user.Identity.Name??"","",login.ErrorMessage);
                return Results.BadRequest(new {success=false,errorMessage=login.ErrorMessage});
            }
            var fresh=await auth.ValidateTokenAsync(GatewayAuthEndpoints.ReadToken(context.Request));
            if(!fresh.Success||fresh.Principal.FindFirstValue("jti")!=user.FindFirstValue("jti")||
                login.User==null||login.User.Id!=user.FindFirstValue(ClaimTypes.NameIdentifier)||login.User.SecurityStamp!=user.FindFirstValue("security_stamp"))return Results.Unauthorized();
            string ticket;
            try{ticket=approvals.Issue(request.ContextId??"",fresh.Principal);}
            catch(InvalidOperationException ex){return Results.BadRequest(new {success=false,errorMessage=ex.Message});}
            await GatewayAuthEndpoints.WriteSecurityAuditAsync(context,audit,"auth.reauth","success","sensitive-operation",user.Identity.Name??"","","");
            return Results.Ok(new {success=true,ticket,expiresInSeconds=120});
        });
        return routes;
    }
}
