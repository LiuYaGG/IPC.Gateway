using System.Security.Claims;
using IPC.Gateway.Core.Application.Gateway;
using IPC.Gateway.Core.Application.Gateway.Contracts;
using IPC.Gateway.Core.Domain.Users;
using IPC.Gateway.WebHost;

namespace IPC.Gateway.Tests;

public sealed class GatewayConfigurationSecretPolicyTests
{
    [Fact]
    public void SanitizeProject_RedactsNestedSecrets()
    {
        ProjectConfigurationDto project = CreateProjectWithSecrets();

        ProjectConfigurationDto sanitized = GatewayConfigurationSecretPolicy.SanitizeProject(project);

        Assert.Equal(GatewayConfigurationSecretPolicy.RedactedSecret, sanitized.Devices[0].Connection.Password);
        Assert.Equal(GatewayConfigurationSecretPolicy.RedactedSecret, sanitized.Rules[0].Actions[0].EmailPassword);
        Assert.Equal(GatewayConfigurationSecretPolicy.RedactedSecret, sanitized.FlowRules[0].Nodes[0].EmailPassword);
    }

    [Fact]
    public void PreserveProjectSecrets_RestoresMaskedValuesAndKeepsExplicitUpdates()
    {
        ProjectConfigurationDto current = CreateProjectWithSecrets();
        SaveProjectConfigurationCommand command = new SaveProjectConfigurationCommand
        {
            Devices = new List<DeviceConfigurationDto>
            {
                new DeviceConfigurationDto
                {
                    Id = "device-1",
                    Name = "Press",
                    Connection = new PlcConnectionDto
                    {
                        Password = GatewayConfigurationSecretPolicy.RedactedSecret
                    }
                }
            },
            Rules = new List<EdgeRuleConfigurationDto>
            {
                new EdgeRuleConfigurationDto
                {
                    Id = "rule-1",
                    Name = "Alarm",
                    Actions = new List<EdgeRuleActionDto>
                    {
                        new EdgeRuleActionDto
                        {
                            Id = "action-1",
                            EmailPassword = GatewayConfigurationSecretPolicy.RedactedSecret
                        }
                    }
                }
            },
            FlowRules = new List<FlowRuleDefinitionDto>
            {
                new FlowRuleDefinitionDto
                {
                    Id = "flow-1",
                    Name = "Flow Alarm",
                    Nodes = new List<FlowRuleNodeDto>
                    {
                        new FlowRuleNodeDto
                        {
                            Id = "node-1",
                            EmailPassword = GatewayConfigurationSecretPolicy.RedactedSecret
                        }
                    }
                }
            }
        };

        GatewayConfigurationSecretPolicy.PreserveProjectSecrets(command, current);

        Assert.Equal("device-password", command.Devices[0].Connection.Password);
        Assert.Equal("rule-email-password", command.Rules[0].Actions[0].EmailPassword);
        Assert.Equal("flow-email-password", command.FlowRules[0].Nodes[0].EmailPassword);
    }

    [Fact]
    public void PreserveMqttSecrets_RestoresOnlyMaskedValues()
    {
        SaveMqttConfigurationCommand command = new SaveMqttConfigurationCommand
        {
            Password = GatewayConfigurationSecretPolicy.RedactedSecret,
            ClientCertificatePassword = "new-cert-password"
        };

        GatewayConfigurationSecretPolicy.PreserveMqttSecrets(command, new MqttConfigurationDto
        {
            Password = "mqtt-password",
            ClientCertificatePassword = "mqtt-cert-password"
        });

        Assert.Equal("mqtt-password", command.Password);
        Assert.Equal("new-cert-password", command.ClientCertificatePassword);
    }

    [Fact]
    public void SanitizeSync_FiltersConfigurationForModuleScopedUser()
    {
        ClaimsPrincipal user = CreateUser(GatewayPermissions.ViewMqtt);
        GatewaySyncDto sync = new GatewaySyncDto
        {
            Project = CreateProjectWithSecrets(),
            Mqtt = new MqttConfigurationDto
            {
                Password = "mqtt-password",
                ClientCertificatePassword = "mqtt-cert-password"
            },
            Status = new GatewayRuntimeStatusDto
            {
                DeviceCount = 1,
                Devices = new List<DeviceRuntimeStatusDto>
                {
                    new DeviceRuntimeStatusDto { DeviceId = "device-1", DeviceName = "Press" }
                },
                Mqtt = new MqttRuntimeStatusDto { Enabled = true, Broker = "broker:1883" }
            }
        };

        GatewaySyncDto sanitized = GatewayConfigurationSecurity.SanitizeSync(sync, user);

        Assert.Empty(sanitized.Project.Devices);
        Assert.Empty(sanitized.Project.Rules);
        Assert.Empty(sanitized.Project.FlowRules);
        Assert.Equal(GatewayConfigurationSecurity.RedactedSecret, sanitized.Mqtt.Password);
        Assert.Equal(GatewayConfigurationSecurity.RedactedSecret, sanitized.Mqtt.ClientCertificatePassword);
        Assert.True(sanitized.Status.Mqtt.Enabled);
        Assert.Empty(sanitized.Status.Devices);
        Assert.Equal(0, sanitized.Status.DeviceCount);
    }

    private static ProjectConfigurationDto CreateProjectWithSecrets()
    {
        return new ProjectConfigurationDto
        {
            ProjectId = "project-1",
            Name = "Line 1",
            Devices = new List<DeviceConfigurationDto>
            {
                new DeviceConfigurationDto
                {
                    Id = "device-1",
                    Name = "Press",
                    Connection = new PlcConnectionDto
                    {
                        Password = "device-password"
                    }
                }
            },
            Rules = new List<EdgeRuleConfigurationDto>
            {
                new EdgeRuleConfigurationDto
                {
                    Id = "rule-1",
                    Name = "Alarm",
                    Actions = new List<EdgeRuleActionDto>
                    {
                        new EdgeRuleActionDto
                        {
                            Id = "action-1",
                            EmailPassword = "rule-email-password"
                        }
                    }
                }
            },
            FlowRules = new List<FlowRuleDefinitionDto>
            {
                new FlowRuleDefinitionDto
                {
                    Id = "flow-1",
                    Name = "Flow Alarm",
                    Nodes = new List<FlowRuleNodeDto>
                    {
                        new FlowRuleNodeDto
                        {
                            Id = "node-1",
                            Label = "Email",
                            EmailPassword = "flow-email-password"
                        }
                    }
                }
            }
        };
    }

    private static ClaimsPrincipal CreateUser(params string[] permissions)
    {
        List<Claim> claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, "operator")
        };
        claims.AddRange(permissions.Select(permission => new Claim("permission", permission)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }
}
