using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;

namespace ServerlessAPI;

public class JwtValidator
{
  private readonly string _jwtSecret;

  public JwtValidator(string jwtSecret)
  {
    _jwtSecret = jwtSecret;
    Console.WriteLine($"JwtValidator initialized with secret: {(string.IsNullOrEmpty(_jwtSecret) ? "EMPTY" : "SET")}");
    Console.Out.Flush();
  }

  public bool ValidateJwtToken(HttpContext context, out string subject)
  {
    subject = string.Empty;

    try
    {
      // Extract token from Authorization header
      var authHeader = context.Request.Headers["Authorization"].ToString();
      if (string.IsNullOrEmpty(authHeader))
      {
        Console.WriteLine("No Authorization header found");
        Console.Out.Flush();
        return false;
      }

      if (!authHeader.StartsWith("Bearer "))
      {
        Console.WriteLine("Invalid Authorization header format");
        Console.Out.Flush();
        return false;
      }

      var token = authHeader.Substring("Bearer ".Length).Trim();

      // Validate JWT
      var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSecret));
      var tokenHandler = new JwtSecurityTokenHandler();

      tokenHandler.ValidateToken(token, new TokenValidationParameters
      {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = key,
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
      }, out SecurityToken validatedToken);

      var jwtToken = (JwtSecurityToken)validatedToken;
      subject = jwtToken.Claims.FirstOrDefault(x => x.Type == "sub")?.Value ?? string.Empty;

      Console.WriteLine($"JWT token validated successfully for subject: {subject}");
      Console.Out.Flush();
      return true;
    }
    catch (Exception ex)
    {
      Console.WriteLine($"JWT validation failed: {ex.Message}, ExceptionType: {ex.GetType().Name}");
      Console.Out.Flush();
      return false;
    }
  }
}
