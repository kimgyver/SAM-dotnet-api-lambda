using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Amazon.SimpleSystemsManagement;
using Microsoft.IdentityModel.Tokens;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace JwtAuthorizer;

public class Handler
{
  private static readonly IAmazonSimpleSystemsManagement _ssmClient = new AmazonSimpleSystemsManagementClient();
  private static string? _cachedSecret;

  public async Task<APIGatewayAuthorizerResponse> HandleRequest(APIGatewayTokenAuthorizerEvent request, ILambdaContext context)
  {
    context.Logger.LogLine($"Authorizing token: {request.AuthorizationToken}");

    try
    {
      // Extract JWT token from "Bearer <token>"
      var token = request.AuthorizationToken.StartsWith("Bearer ")
          ? request.AuthorizationToken.Substring(7)
          : request.AuthorizationToken;

      // Get JWT secret from environment or Parameter Store
      var secret = await GetJwtSecret();

      // Validate JWT
      var principal = ValidateJwt(token, secret);

      // Return authorized response
      return new APIGatewayAuthorizerResponse
      {
        PrincipalID = principal.Subject,
        PolicyDocument = new APIGatewayAuthorizerResponse.AuthPolicy
        {
          Version = "2012-10-17",
          Statement = new List<APIGatewayAuthorizerResponse.IAMPolicyStatement>
                    {
                        new APIGatewayAuthorizerResponse.IAMPolicyStatement
                        {
                            Action = "execute-api:Invoke",
                            Effect = "Allow",
                            Resource = request.MethodArn
                        }
                    }
        },
        Context = new Dictionary<string, object>
                {
                    { "Subject", principal.Subject }
                }
      };
    }
    catch (Exception ex)
    {
      context.Logger.LogLine($"Unauthorized: {ex.Message}");
      throw new UnauthorizedAccessException("Unauthorized");
    }
  }

  private JwtSecurityToken ValidateJwt(string token, string secret)
  {
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    var tokenHandler = new JwtSecurityTokenHandler();

    try
    {
      tokenHandler.ValidateToken(token, new TokenValidationParameters
      {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = key,
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
      }, out SecurityToken validatedToken);

      return (JwtSecurityToken)validatedToken;
    }
    catch (Exception ex)
    {
      throw new InvalidOperationException($"JWT validation failed: {ex.Message}", ex);
    }
  }

  private async Task<string> GetJwtSecret()
  {
    // Try to get from environment first
    var envSecret = Environment.GetEnvironmentVariable("JWT_SECRET");
    if (!string.IsNullOrEmpty(envSecret))
    {
      return envSecret;
    }

    // Fall back to Parameter Store
    if (_cachedSecret == null)
    {
      var request = new Amazon.SimpleSystemsManagement.Model.GetParameterRequest
      {
        Name = "/serverless-api/jwt-secret",
        WithDecryption = true
      };

      var response = await _ssmClient.GetParameterAsync(request);
      _cachedSecret = response.Parameter.Value;
    }

    return _cachedSecret;
  }
}

public class APIGatewayTokenAuthorizerEvent
{
  public string AuthorizationToken { get; set; } = "";
  public string MethodArn { get; set; } = "";
}

public class APIGatewayAuthorizerResponse
{
  public string PrincipalID { get; set; } = "";
  public AuthPolicy PolicyDocument { get; set; } = new();
  public Dictionary<string, object> Context { get; set; } = new();

  public class AuthPolicy
  {
    public string Version { get; set; } = "2012-10-17";
    public List<IAMPolicyStatement> Statement { get; set; } = new();
  }

  public class IAMPolicyStatement
  {
    public string Action { get; set; } = "";
    public string Effect { get; set; } = "";
    public string Resource { get; set; } = "";
  }
}
