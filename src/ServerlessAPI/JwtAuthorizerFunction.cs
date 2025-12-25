using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using Amazon.Lambda.Core;
using Microsoft.IdentityModel.Tokens;

namespace ServerlessAPI;

public class JwtAuthorizerFunction
{
  private readonly string _jwtSecret;

  public JwtAuthorizerFunction()
  {
    _jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET") ?? "your-secret-key-change-this-in-production";
    Console.WriteLine($"JwtAuthorizerFunction initialized");
    Console.Out.Flush();
  }

  public object FunctionHandler(HttpApiV2AuthorizerRequest request, ILambdaContext context)
  {
    Console.WriteLine($"Authorizer received request from: {request.RequestContext?.Http?.Method} {request.RequestContext?.Http?.Path}");
    Console.Out.Flush();

    try
    {
      // Extract token from Authorization header
      var authHeader = request.Headers?.ContainsKey("authorization") == true
          ? request.Headers["authorization"]
          : null;

      if (string.IsNullOrEmpty(authHeader))
      {
        Console.WriteLine("No Authorization header found");
        Console.Out.Flush();
        throw new UnauthorizedAccessException("Missing authorization header");
      }

      var token = authHeader;
      if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
      {
        token = token.Substring("Bearer ".Length).Trim();
      }

      // Validate JWT
      var tokenHandler = new JwtSecurityTokenHandler();
      var key = Encoding.ASCII.GetBytes(_jwtSecret);

      tokenHandler.ValidateToken(token, new TokenValidationParameters
      {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
      }, out SecurityToken validatedToken);

      var jwtToken = (JwtSecurityToken)validatedToken;
      var subject = jwtToken.Claims.FirstOrDefault(x => x.Type == "sub")?.Value ?? "unknown";
      var role = jwtToken.Claims.FirstOrDefault(x => x.Type == "role")?.Value ?? "user";

      Console.WriteLine($"Token validated successfully for subject: {subject}, role: {role}");
      Console.Out.Flush();

      // Return simple response format for HTTP API v2
      // The context values will be available in the request to the Lambda function
      return new
      {
        isAuthorized = true,
        context = new
        {
          subject = subject,
          role = role,
          principalId = subject
        }
      };
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Authorization failed: {ex.Message}");
      Console.Out.Flush();
      return new { isAuthorized = false };
    }
  }
}

public class HttpApiV2AuthorizerRequest
{
  public string? Version { get; set; }
  public string? RouteKey { get; set; }
  public string? RawPath { get; set; }
  public string? RawQueryString { get; set; }
  public Dictionary<string, string>? Headers { get; set; }
  public RequestContextData? RequestContext { get; set; }
}

public class RequestContextData
{
  public HttpData? Http { get; set; }
  public string? AccountId { get; set; }
  public string? ApiId { get; set; }
  public string? DomainName { get; set; }
  public long? TimeEpoch { get; set; }
}

public class HttpData
{
  public string? Method { get; set; }
  public string? Path { get; set; }
  public string? Protocol { get; set; }
  public string? SourceIp { get; set; }
  public string? UserAgent { get; set; }
}

public class AuthorizerResponse
{
  [System.Text.Json.Serialization.JsonPropertyName("isAuthorized")]
  public bool IsAuthorized { get; set; }

  [System.Text.Json.Serialization.JsonPropertyName("context")]
  public Dictionary<string, object>? Context { get; set; }
}