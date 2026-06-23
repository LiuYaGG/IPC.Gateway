/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.WebHost
* 项目描述 ：
* 类 名 称 ：GatewayAuthEndpoints
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.WebHost
* 机器名称 ：UNKNOWN 
* CLR 版本 ：10.0.0
* 作    者 ：ipc
* 创建时间 ：2026-06-23 17:52:06
* 更新时间 ：2026-06-23 17:52:06
* 版 本 号 ：v1.0.0.0
*******************************************************************
* Copyright @ ipc 2026. All rights reserved.
*******************************************************************
//----------------------------------------------------------------*/
using System.Security.Claims;
using IPC.Gateway.Core.Application.Users;
using IPC.Gateway.Core.Domain.Users;
using IPC.Gateway.Core.Gateway;

namespace IPC.Gateway.WebHost;

public static class GatewayAuthEndpoints
{
    private const string CookieName = "ipc_gateway_token";

    public static IEndpointRouteBuilder MapGatewayAuthEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/auth");

        group.MapPost("/login", (GatewayAuthService auth, HttpContext context, IGatewayAuditLogStore auditStore, GatewayLoginRequest request) =>
        {
            GatewayLoginResult result = auth.Login(request.Username, request.Password);
            if (!result.Success)
            {
                WriteSecurityAudit(context, auditStore, "login", result.Locked ? "locked" : "failed", "auth:login", request.Username, string.Empty, result.ErrorMessage);
                return Results.Json(new
                {
                    success = false,
                    errorMessage = string.IsNullOrWhiteSpace(result.ErrorMessage) ? "账号或密码错误。" : result.ErrorMessage,
                    locked = result.Locked,
                    lockoutEndTime = result.LockoutEndTime
                }, statusCode: StatusCodes.Status401Unauthorized);
            }

            context.Response.Cookies.Append(CookieName, result.Token, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Secure = context.Request.IsHttps,
                Expires = result.ExpiresAt,
                Path = "/"
            });

            WriteSecurityAudit(context, auditStore, "login", "success", "auth:login", result.User?.Username ?? request.Username, result.User?.Role ?? string.Empty, string.Empty);
            return Results.Ok(result);
        });

        group.MapPost("/logout", (HttpContext context, IGatewayAuditLogStore auditStore) =>
        {
            WriteSecurityAudit(context, auditStore, "logout", "success", "auth:logout", context.User?.Identity?.Name ?? string.Empty, context.User?.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty, string.Empty);
            context.Response.Cookies.Delete(CookieName);
            return Results.Ok(new { success = true });
        });

        group.MapGet("/me", (GatewayAuthService auth, ClaimsPrincipal user) =>
        {
            if (user?.Identity?.IsAuthenticated != true)
                return Results.Unauthorized();

            return Results.Ok(new
            {
                success = true,
                user = auth.GetCurrentUser(user),
                permissions = GetClaimPermissions(user)
            });
        });

        group.MapPut("/password", (IGatewayUserApplicationService users, ClaimsPrincipal user, HttpContext context, IGatewayAuditLogStore auditStore, GatewayPasswordChangeRequest request) =>
        {
            if (user?.Identity?.IsAuthenticated != true)
                return Results.Unauthorized();

            string username = user.Identity?.Name ?? string.Empty;
            return ExecuteSecurityAction(context, auditStore, "auth.password.change", "user:" + username, () =>
            {
                users.ChangePassword(username, request.CurrentPassword, request.NewPassword);
                return new { changed = true };
            });
        });

        group.MapGet("/users", (IGatewayUserApplicationService users, ClaimsPrincipal user) =>
        {
            if (!CanViewUsers(user))
                return Results.Json(new { success = false, errorMessage = "当前用户没有人员管理权限。" }, statusCode: StatusCodes.Status403Forbidden);
            return Results.Ok(new { success = true, data = users.GetUsers() });
        });

        group.MapPost("/users", (IGatewayUserApplicationService users, ClaimsPrincipal user, HttpContext context, IGatewayAuditLogStore auditStore, GatewayUserSaveRequest request) =>
        {
            if (!CanCreateUser(user))
                return ForbiddenSecurityAction(context, auditStore, "users.create", "user:" + request.Username, "当前用户没有新增人员权限。");
            return ExecuteSecurityAction(context, auditStore, "users.create", "user:" + request.Username, () => users.SaveUser(request.Username, request.DisplayName, request.Role, request.Enabled, request.Password));
        });

        group.MapPut("/users/{username}", (IGatewayUserApplicationService users, ClaimsPrincipal user, HttpContext context, IGatewayAuditLogStore auditStore, string username, GatewayUserSaveRequest request) =>
        {
            if (!CanEditUser(user))
                return ForbiddenSecurityAction(context, auditStore, "users.edit", "user:" + username, "当前用户没有编辑人员权限。");
            return ExecuteSecurityAction(context, auditStore, "users.edit", "user:" + username, () => users.SaveUser(username, request.DisplayName, request.Role, request.Enabled, request.Password));
        });

        group.MapPut("/users/{username}/password", (IGatewayUserApplicationService users, ClaimsPrincipal user, HttpContext context, IGatewayAuditLogStore auditStore, string username, GatewayUserPasswordResetRequest request) =>
        {
            if (!CanResetUserPassword(user))
                return ForbiddenSecurityAction(context, auditStore, "users.password.reset", "user:" + username, "当前用户没有重置人员密码权限。");
            return ExecuteSecurityAction(context, auditStore, "users.password.reset", "user:" + username, () => users.ResetPassword(username, request.NewPassword));
        });

        group.MapDelete("/users/{username}", (IGatewayUserApplicationService users, ClaimsPrincipal user, HttpContext context, IGatewayAuditLogStore auditStore, string username) =>
        {
            if (!CanDeleteUser(user))
                return ForbiddenSecurityAction(context, auditStore, "users.delete", "user:" + username, "当前用户没有删除人员权限。");
            return ExecuteSecurityAction(context, auditStore, "users.delete", "user:" + username, () =>
            {
                users.DeleteUser(username);
                return new { deleted = true };
            });
        });

        group.MapGet("/permissions", (IGatewayRoleApplicationService roles, ClaimsPrincipal user) =>
        {
            if (!CanViewPermissions(user))
                return Results.Json(new { success = false, errorMessage = "当前用户没有查看权限分配的权限。" }, statusCode: StatusCodes.Status403Forbidden);
            return Results.Ok(new { success = true, data = roles.GetPermissionCatalog() });
        });

        group.MapGet("/roles", (IGatewayRoleApplicationService roles, ClaimsPrincipal user) =>
        {
            if (!CanViewRoles(user) && !CanViewUsers(user) && !CanViewPermissions(user))
                return Results.Json(new { success = false, errorMessage = "当前用户没有查看角色权限。" }, statusCode: StatusCodes.Status403Forbidden);
            return Results.Ok(new { success = true, data = roles.GetRoles() });
        });

        group.MapPost("/roles", (IGatewayRoleApplicationService roles, ClaimsPrincipal user, HttpContext context, IGatewayAuditLogStore auditStore, GatewayRoleSaveRequest request) =>
        {
            if (!CanCreateRole(user))
                return ForbiddenSecurityAction(context, auditStore, "roles.create", "role:" + request.Name, "当前用户没有新增角色权限。");

            IEnumerable<string> permissions = CanEditPermissions(user) ? request.Permissions : Array.Empty<string>();
            return ExecuteSecurityAction(context, auditStore, "roles.create", "role:" + request.Name, () => roles.SaveRole(request.Name, request.DisplayName, request.Description, request.Enabled, permissions));
        });

        group.MapPut("/roles/{roleName}", (IGatewayRoleApplicationService roles, ClaimsPrincipal user, HttpContext context, IGatewayAuditLogStore auditStore, string roleName, GatewayRoleSaveRequest request) =>
        {
            if (!CanEditRole(user))
                return ForbiddenSecurityAction(context, auditStore, "roles.edit", "role:" + roleName, "当前用户没有编辑角色权限。");

            IEnumerable<string> permissions = CanEditPermissions(user)
                ? request.Permissions
                : roles.FindByName(roleName)?.Permissions ?? Array.Empty<string>();
            return ExecuteSecurityAction(context, auditStore, "roles.edit", "role:" + roleName, () => roles.SaveRole(roleName, request.DisplayName, request.Description, request.Enabled, permissions));
        });

        group.MapPut("/roles/{roleName}/permissions", (IGatewayRoleApplicationService roles, ClaimsPrincipal user, HttpContext context, IGatewayAuditLogStore auditStore, string roleName, GatewayRolePermissionSaveRequest request) =>
        {
            if (!CanEditPermissions(user))
                return ForbiddenSecurityAction(context, auditStore, "roles.permissions.edit", "role:" + roleName, "当前用户没有保存权限分配的权限。");

            return ExecuteSecurityAction(context, auditStore, "roles.permissions.edit", "role:" + roleName, () =>
            {
                GatewayRoleInfo? role = roles.FindByName(roleName);
                if (role == null)
                    throw new InvalidOperationException("角色不存在：" + roleName);
                return roles.SaveRole(roleName, role.DisplayName, role.Description, role.Enabled, request.Permissions);
            });
        });

        group.MapDelete("/roles/{roleName}", (IGatewayRoleApplicationService roles, ClaimsPrincipal user, HttpContext context, IGatewayAuditLogStore auditStore, string roleName) =>
        {
            if (!CanDeleteRole(user))
                return ForbiddenSecurityAction(context, auditStore, "roles.delete", "role:" + roleName, "当前用户没有删除角色权限。");

            return ExecuteSecurityAction(context, auditStore, "roles.delete", "role:" + roleName, () =>
            {
                roles.DeleteRole(roleName);
                return new { deleted = true };
            });
        });

        return app;
    }

    public static string ReadToken(HttpRequest request)
    {
        string token = request.Cookies[CookieName] ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(token))
            return token;

        string authorization = request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return authorization.Substring("Bearer ".Length).Trim();
        return string.Empty;
    }

    public static bool IsAdmin(ClaimsPrincipal user)
    {
        return user != null && user.IsInRole("Admin");
    }

    public static bool CanManageUsers(ClaimsPrincipal user)
    {
        return CanViewUsers(user) || CanCreateUser(user) || CanEditUser(user) || CanResetUserPassword(user) || CanDeleteUser(user);
    }

    public static bool CanManageRoles(ClaimsPrincipal user)
    {
        return CanViewRoles(user) || CanCreateRole(user) || CanEditRole(user) || CanDeleteRole(user) || CanViewPermissions(user) || CanEditPermissions(user);
    }

    public static bool CanViewDashboard(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.ViewDashboard, GatewayPermissions.ViewRuntime);
    }

    public static bool CanViewDevices(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.ViewDevices, GatewayPermissions.ReadConfiguration, GatewayPermissions.ViewRuntime);
    }

    public static bool CanCreateDevice(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.CreateDevice, GatewayPermissions.WriteConfiguration);
    }

    public static bool CanEditDevice(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.EditDevice, GatewayPermissions.WriteConfiguration);
    }

    public static bool CanDeleteDevice(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.DeleteDevice, GatewayPermissions.WriteConfiguration);
    }

    public static bool CanCreateGroup(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.CreateGroup, GatewayPermissions.WriteConfiguration);
    }

    public static bool CanEditGroup(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.EditGroup, GatewayPermissions.WriteConfiguration);
    }

    public static bool CanDeleteGroup(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.DeleteGroup, GatewayPermissions.WriteConfiguration);
    }

    public static bool CanCreateTag(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.CreateTag, GatewayPermissions.WriteConfiguration);
    }

    public static bool CanEditTag(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.EditTag, GatewayPermissions.WriteConfiguration);
    }

    public static bool CanDeleteTag(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.DeleteTag, GatewayPermissions.WriteConfiguration);
    }

    public static bool CanWriteTag(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.WriteTag, GatewayPermissions.WriteRuntime);
    }

    public static bool CanViewRules(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.ViewRules, GatewayPermissions.ReadConfiguration);
    }

    public static bool CanCreateRule(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.CreateRule, GatewayPermissions.WriteConfiguration);
    }

    public static bool CanEditRule(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.EditRule, GatewayPermissions.WriteConfiguration);
    }

    public static bool CanDeleteRule(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.DeleteRule, GatewayPermissions.WriteConfiguration);
    }

    public static bool CanDebugRule(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.DebugRule, GatewayPermissions.WriteConfiguration);
    }

    public static bool CanViewFlowRules(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.ViewFlowRules, GatewayPermissions.ReadConfiguration);
    }

    public static bool CanCreateFlowRule(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.CreateFlowRule, GatewayPermissions.WriteConfiguration);
    }

    public static bool CanEditFlowRule(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.EditFlowRule, GatewayPermissions.WriteConfiguration);
    }

    public static bool CanDeleteFlowRule(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.DeleteFlowRule, GatewayPermissions.WriteConfiguration);
    }

    public static bool CanDebugFlowRule(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.DebugFlowRule, GatewayPermissions.WriteConfiguration);
    }

    public static bool CanViewMqtt(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.ViewMqtt, GatewayPermissions.ReadConfiguration);
    }

    public static bool CanEditMqtt(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.EditMqtt, GatewayPermissions.WriteConfiguration);
    }

    public static bool CanViewOpcUa(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.ViewOpcUa, GatewayPermissions.ReadConfiguration);
    }

    public static bool CanEditOpcUa(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.EditOpcUa, GatewayPermissions.WriteConfiguration);
    }

    public static bool CanViewProject(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.ViewProject, GatewayPermissions.ReadConfiguration);
    }

    public static bool CanEditProject(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.EditProject, GatewayPermissions.WriteConfiguration);
    }

    public static bool CanEditHistory(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.EditHistory, GatewayPermissions.EditDashboardStorageHealth, GatewayPermissions.WriteConfiguration);
    }

    public static bool CanViewUsers(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.ViewUsers, GatewayPermissions.ManageUsers);
    }

    public static bool CanCreateUser(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.CreateUser, GatewayPermissions.ManageUsers);
    }

    public static bool CanEditUser(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.EditUser, GatewayPermissions.ManageUsers);
    }

    public static bool CanResetUserPassword(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.ResetUserPassword, GatewayPermissions.ManageUsers);
    }

    public static bool CanDeleteUser(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.DeleteUser, GatewayPermissions.ManageUsers);
    }

    public static bool CanViewRoles(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.ViewRoles, GatewayPermissions.ManageRoles);
    }

    public static bool CanCreateRole(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.CreateRole, GatewayPermissions.ManageRoles);
    }

    public static bool CanEditRole(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.EditRole, GatewayPermissions.ManageRoles);
    }

    public static bool CanDeleteRole(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.DeleteRole, GatewayPermissions.ManageRoles);
    }

    public static bool CanViewPermissions(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.ViewPermissions, GatewayPermissions.ManageRoles);
    }

    public static bool CanEditPermissions(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.EditPermissions, GatewayPermissions.ManageRoles);
    }

    public static bool CanViewAudit(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasPermission(user, GatewayPermissions.ViewAudit);
    }

    public static bool CanExportAudit(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasPermission(user, GatewayPermissions.ExportAudit);
    }

    public static bool CanViewSecurity(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.ViewSecurity, GatewayPermissions.ManageCertificates);
    }

    public static bool CanManageCertificates(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasPermission(user, GatewayPermissions.ManageCertificates);
    }

    public static bool CanViewMaintenance(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasPermission(user, GatewayPermissions.ViewMaintenance);
    }

    public static bool CanUploadUpdatePackage(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasPermission(user, GatewayPermissions.UploadUpdatePackage);
    }

    public static bool CanPrepareUpdate(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasPermission(user, GatewayPermissions.PrepareUpdate);
    }

    public static bool CanRollbackUpdate(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasPermission(user, GatewayPermissions.RollbackUpdate);
    }

    public static bool CanEditWatchdog(ClaimsPrincipal user)
    {
        return IsAdmin(user) || HasAnyPermission(user, GatewayPermissions.EditWatchdog, GatewayPermissions.WriteConfiguration);
    }

    public static bool CanWriteConfiguration(ClaimsPrincipal user)
    {
        return user != null && (
            IsAdmin(user) ||
            HasAnyPermission(user,
                GatewayPermissions.WriteConfiguration,
                GatewayPermissions.EditDashboardStorageHealth,
                GatewayPermissions.CreateDevice,
                GatewayPermissions.EditDevice,
                GatewayPermissions.DeleteDevice,
                GatewayPermissions.CreateGroup,
                GatewayPermissions.EditGroup,
                GatewayPermissions.DeleteGroup,
                GatewayPermissions.CreateTag,
                GatewayPermissions.EditTag,
                GatewayPermissions.DeleteTag,
                GatewayPermissions.WriteTag,
                GatewayPermissions.CreateRule,
                GatewayPermissions.EditRule,
                GatewayPermissions.DeleteRule,
                GatewayPermissions.CreateFlowRule,
                GatewayPermissions.EditFlowRule,
                GatewayPermissions.DeleteFlowRule,
                GatewayPermissions.EditMqtt,
                GatewayPermissions.EditOpcUa,
                GatewayPermissions.EditProject,
                GatewayPermissions.EditHistory,
                GatewayPermissions.EditWatchdog));
    }

    public static bool HasAnyPermission(ClaimsPrincipal user, params string[] permissions)
    {
        return permissions.Any(permission => HasPermission(user, permission));
    }

    public static bool HasPermission(ClaimsPrincipal user, string permission)
    {
        return user != null &&
               user.Claims.Any(claim =>
                   claim.Type.Equals("permission", StringComparison.OrdinalIgnoreCase) &&
                   claim.Value.Equals(permission, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> GetClaimPermissions(ClaimsPrincipal user)
    {
        return (user?.Claims ?? Enumerable.Empty<Claim>())
            .Where(claim => claim.Type.Equals("permission", StringComparison.OrdinalIgnoreCase))
            .Select(claim => claim.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool IsPublicRequest(HttpRequest request)
    {
        string path = request.Path.Value ?? string.Empty;
        if (IsHealthRequest(path))
        {
            GatewayIndustrialSecurityOptions? security = request.HttpContext.RequestServices.GetService<GatewayIndustrialSecurityOptions>();
            return security?.Api.RequireAuthenticationForHealth != true;
        }

        if (path.Equals("/", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/index.html", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            return true;

        return path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHealthRequest(string path)
    {
        return path.Equals("/health", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("/health/", StringComparison.OrdinalIgnoreCase) ||
               path.Equals("/api/health", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("/api/health/", StringComparison.OrdinalIgnoreCase);
    }

    private static IResult ForbiddenSecurityAction(HttpContext context, IGatewayAuditLogStore? auditStore, string action, string target, string message)
    {
        WriteSecurityAudit(context, auditStore, action, "forbidden", target, string.Empty, string.Empty, message);
        return Results.Json(new { success = false, errorMessage = message }, statusCode: StatusCodes.Status403Forbidden);
    }

    private static IResult ExecuteSecurityAction(HttpContext context, IGatewayAuditLogStore? auditStore, string action, string target, Func<object?> handler)
    {
        try
        {
            object? data = handler();
            WriteSecurityAudit(context, auditStore, action, "success", target, string.Empty, string.Empty, string.Empty);
            return Results.Ok(new { success = true, data });
        }
        catch (ArgumentException ex)
        {
            WriteSecurityAudit(context, auditStore, action, "bad_request", target, string.Empty, string.Empty, ex.Message);
            return Results.BadRequest(new { success = false, errorMessage = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            WriteSecurityAudit(context, auditStore, action, "bad_request", target, string.Empty, string.Empty, ex.Message);
            return Results.BadRequest(new { success = false, errorMessage = ex.Message });
        }
        catch (Exception ex)
        {
            WriteSecurityAudit(context, auditStore, action, "error", target, string.Empty, string.Empty, ex.Message);
            throw;
        }
    }

    private static IResult ExecuteRoleAction(Func<object?> action)
    {
        try
        {
            return Results.Ok(new { success = true, data = action() });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { success = false, errorMessage = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { success = false, errorMessage = ex.Message });
        }
    }

    private static IResult ExecuteUserAction(Func<object?> action)
    {
        try
        {
            return Results.Ok(new { success = true, data = action() });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { success = false, errorMessage = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { success = false, errorMessage = ex.Message });
        }
    }

    internal static void WriteSecurityAudit(HttpContext context, IGatewayAuditLogStore? auditStore, string action, string outcome, string target, string username, string role, string errorMessage)
    {
        try
        {
            GatewayAuditLog.WriteSecurityEvent(new GatewaySecurityAuditEvent
            {
                Action = action,
                Outcome = outcome,
                Target = target,
                UserName = string.IsNullOrWhiteSpace(username) ? context.User?.Identity?.Name ?? string.Empty : username,
                Role = string.IsNullOrWhiteSpace(role) ? context.User?.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty : role,
                RemoteIpAddress = context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
                Method = context.Request.Method,
                Path = context.Request.Path.Value ?? string.Empty,
                TraceId = context.TraceIdentifier,
                ErrorMessage = errorMessage ?? string.Empty
            }, auditStore);
        }
        catch
        {
        }
    }
}

public sealed class GatewayUserSaveRequest
{
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = "Viewer";
    public bool Enabled { get; set; } = true;
    public string Password { get; set; } = string.Empty;
}

public sealed class GatewayPasswordChangeRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class GatewayUserPasswordResetRequest
{
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class GatewayRoleSaveRequest
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public IList<string> Permissions { get; set; } = new List<string>();
}

public sealed class GatewayRolePermissionSaveRequest
{
    public IList<string> Permissions { get; set; } = new List<string>();
}
