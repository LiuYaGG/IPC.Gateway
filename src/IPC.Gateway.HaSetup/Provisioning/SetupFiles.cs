using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace IPC.Gateway.HaSetup;

public sealed record RoleSettings(string NodeId, string ClientUrl, string PeerUrl, string? GatewayUrl);
public sealed record RoleChoice(string NodeId);
public sealed record Member(string NodeId,string ClientUrl,string PeerUrl,string? GatewayUrl,
    string CertificateSha256,string? GatewayCertificateSha256);
public sealed record Peer(string NodeId,string Address,string Code);
public sealed record CreateCluster(string ClusterId,string Name,string BusinessUrl,string PreferredNode,Peer[] Peers);
internal sealed record Plan(string ClusterId,string Name,string BusinessUrl,string PreferredNode,Member[] Members);
internal sealed record ApplyPlan(string NodeId,Plan Plan);
public sealed record SetupStatus(Member[] Roles,string? ClusterId,bool Configured,int PairPort);
public sealed record PairingCode(string Code,DateTimeOffset ExpiresAt);
public sealed record SetupCommitResult(Member[] Members,string ConfigDirectory);

internal sealed class SetupFiles(string root)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented=true };
    public string Root { get; } = Path.GetFullPath(root);
    public static readonly string[] Roles = ["A","B","C"];
    public string Folder(string role)
    {
        if (!Roles.Contains(role)) throw new ArgumentException("请选择 A、B 或 C。");
        return Path.Combine(Root,role);
    }
    public IEnumerable<Member> Identities() => Roles.Select(r=>Path.Combine(Folder(r),"identity.setup.json"))
        .Where(File.Exists).Select(path=>JsonSerializer.Deserialize<Member>(File.ReadAllBytes(path),Json) ?? throw new InvalidDataException("本机身份文件损坏。"));
    public Plan? InstalledPlan(string role)
    {
        var path=Path.Combine(Folder(role),"topology.setup.json");
        return File.Exists(path) ? JsonSerializer.Deserialize<Plan>(File.ReadAllBytes(path),Json) : null;
    }
    public static Uri Endpoint(string? value,bool tls)
    {
        if (!Uri.TryCreate(value,UriKind.Absolute,out var url) || (url.Scheme!="https" && (tls || url.Scheme!="http")) ||
            url.AbsolutePath!="/" || url.UserInfo.Length>0 || url.Query.Length>0 || url.Fragment.Length>0)
            throw new ArgumentException("请填写完整的主机地址和端口。仲裁地址必须使用 HTTPS。");
        return url;
    }
    public static void WriteNew(string path,byte[] content)
    {
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            var opts=new FileStreamOptions{Mode=FileMode.CreateNew,Access=FileAccess.Write,Share=FileShare.None};
            if(!OperatingSystem.IsWindows()) opts.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;
            using(var f=new FileStream(temp,opts)){f.Write(content);f.Flush(true);}
            File.Move(temp,path);
        }
        finally {File.Delete(temp);}
    }
    internal static void CreatePrivateDirectory(string path)
    {
        if(!OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
            return;
        }
        var acl=new DirectorySecurity();
        acl.SetAccessRuleProtection(true,false);
        using var identity=WindowsIdentity.GetCurrent();
        foreach(var sid in new[]{identity.User!,new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null)})
            acl.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        new DirectoryInfo(path).Create(acl);
    }
    public static X509Certificate2 Certificate(string subject,IEnumerable<string> hosts)
    {
        using var rsa=RSA.Create(3072);
        var csr=new CertificateRequest("CN="+subject,rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        var san=new SubjectAlternativeNameBuilder();
        foreach(var host in hosts.Distinct(StringComparer.OrdinalIgnoreCase))
            if(IPAddress.TryParse(host.Trim('[',']'),out var ip))san.AddIpAddress(ip);else san.AddDnsName(host);
        csr.CertificateExtensions.Add(san.Build());
        csr.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,false,0,true));
        csr.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature|X509KeyUsageFlags.KeyEncipherment,true));
        csr.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection{
            new("1.3.6.1.5.5.7.3.1"),new("1.3.6.1.5.5.7.3.2")},true));
        return csr.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5),DateTimeOffset.UtcNow.AddYears(2));
    }
    public Member CreateIdentity(RoleSettings req)
    {
        var folder=Folder(req.NodeId);
        var client=Endpoint(req.ClientUrl,true);var peer=Endpoint(req.PeerUrl,true);
        if(client.Port==peer.Port)throw new ArgumentException("成员监听端口必须不同。");
        var web=req.NodeId=="C"?null:Endpoint(req.GatewayUrl,false);
        var existing=Identities().SingleOrDefault(m=>m.NodeId==req.NodeId);
        if(existing!=null)
        {
            if(existing.ClientUrl==client.AbsoluteUri && existing.PeerUrl==peer.AbsoluteUri && existing.GatewayUrl==web?.AbsoluteUri)return existing;
            throw new InvalidOperationException("该角色已有身份，请使用原地址。");
        }
        if(Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            throw new InvalidOperationException("角色目录已有部署文件。重新发布时请在安装程序中选择原成员配置。");
        if(Identities().Any(m=>m.NodeId!="C" && req.NodeId!="C"))throw new ArgumentException("每台机器只可配置一个业务网关。");
        if(Identities().Any(m=>new[]{new Uri(m.ClientUrl).Port,new Uri(m.PeerUrl).Port}.Intersect([client.Port,peer.Port]).Any()))
            throw new ArgumentException("本机的多个成员需要各自独立的端口。");
        string staging=Path.Combine(Root,".identity-"+Guid.NewGuid().ToString("N"));
        CreatePrivateDirectory(staging);
        try
        {
            string Make(string type)
            {
                using var cert=Certificate("IPC-"+req.NodeId+"-"+type,[client.IdnHost,peer.IdnHost]);
                string pass=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                WriteNew(Path.Combine(staging,type+".password"),System.Text.Encoding.UTF8.GetBytes(pass));
                WriteNew(Path.Combine(staging,type+".pfx"),cert.Export(X509ContentType.Pfx,pass));
                return cert.GetCertHashString(HashAlgorithmName.SHA256);
            }
            var result=new Member(req.NodeId,client.AbsoluteUri,peer.AbsoluteUri,web?.AbsoluteUri,Make("member"),web==null?null:Make("gateway"));
            WriteNew(Path.Combine(staging,"identity.setup.json"),JsonSerializer.SerializeToUtf8Bytes(result,Json));
            if(Directory.Exists(folder))Directory.Delete(folder,false);
            Directory.Move(staging,folder);
            return result;
        }
        finally {if(Directory.Exists(staging))Directory.Delete(staging,true);}
    }
    public static void Validate(Plan plan)
    {
        if(string.IsNullOrWhiteSpace(plan.ClusterId)||plan.ClusterId.Length>128||plan.ClusterId.Any(char.IsControl)||
            string.IsNullOrWhiteSpace(plan.Name)||plan.Name.Length>128||plan.Name.Any(char.IsControl)||plan.PreferredNode is not ("A" or "B"))
            throw new ArgumentException("请核对集群名称、ID 和首选节点。");
        Endpoint(plan.BusinessUrl,false);
        if(plan.Members is not {Length:3} || plan.Members.Any(m=>m is null) || !plan.Members.Select(m=>m.NodeId).ToHashSet().SetEquals(Roles))throw new ArgumentException("集群需要唯一的 A、B、C。");
        var urls=new HashSet<Uri>();var pins=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var m in plan.Members)
        {
            if(!urls.Add(Endpoint(m.ClientUrl,true))||!urls.Add(Endpoint(m.PeerUrl,true)))throw new ArgumentException("成员端点重复。");
            if(m.NodeId!="C")Endpoint(m.GatewayUrl,false);
            if(m.NodeId=="C" && (m.GatewayUrl!=null||m.GatewayCertificateSha256!=null))throw new ArgumentException("C 只运行仲裁成员。");
            foreach(var pin in m.NodeId=="C"?new[]{m.CertificateSha256}:new[]{m.CertificateSha256,m.GatewayCertificateSha256})
                if(pin?.Length!=64||!pin.All(char.IsAsciiHexDigit)||!pins.Add(pin))throw new ArgumentException("证书指纹无效或重复。");
        }
    }
    public void Install(string role,Plan plan)
    {
        Validate(plan);
        var local=Identities().Single(m=>m.NodeId==role);
        if(plan.Members.Single(m=>m.NodeId==role)!=local)throw new ArgumentException("计划与本机生成的身份不一致。");
        var dir=Folder(role);
        void Ensure(string file,object content)
        {
            string path=Path.Combine(dir,file);
            byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(content,Json);
            if(!File.Exists(path))WriteNew(path,bytes);
            else if(!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))throw new InvalidOperationException("本机已绑定其他配置，请核对原计划。");
        }
        var members=plan.Members.Select(m=>new {m.NodeId,m.ClientUrl,m.PeerUrl,m.GatewayUrl,m.CertificateSha256,m.GatewayCertificateSha256,CertificateRef=m.CertificateSha256}).ToArray();
        Ensure("member.json",new {Arbiter=new {plan.ClusterId,NodeId=role,StateDirectory=Path.Combine(dir,"raft"),CertificatePath=Path.Combine(dir,"member.pfx"),
            CertificatePasswordFile=Path.Combine(dir,"member.password"),AllowInitialize=true,Members=members}});
        if(role!="C") Ensure("gateway-ha.json",new {Gateway=new {HighAvailability=new {plan.ClusterId,plan.Name,plan.BusinessUrl,plan.PreferredNode,
            NodeId=role,Mode=role=="A"?"Standalone":"Cluster",Endpoints=members.Select(m=>m.ClientUrl),Members=members,
            StateDirectory=Path.Combine(dir,"gateway-state"),TrustRegisteredMemberPins=true,CertificatePath=Path.Combine(dir,"gateway.pfx"),
            CertificatePasswordFile=Path.Combine(dir,"gateway.password")}}});
        Ensure("topology.setup.json",plan);
    }
}
