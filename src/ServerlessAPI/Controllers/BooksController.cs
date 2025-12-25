using Amazon.DynamoDBv2.DataModel;
using Microsoft.AspNetCore.Mvc;
using ServerlessAPI.Entities;
using ServerlessAPI.Repositories;
using System.IdentityModel.Tokens.Jwt;

namespace ServerlessAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class BooksController : ControllerBase
{
    private readonly IBookRepository bookRepository;

    public BooksController(IBookRepository bookRepository)
    {
        this.bookRepository = bookRepository;
    }

    // Helper method to extract claims from JWT token
    private (string subject, string role) ExtractClaimsFromJwt()
    {
        var subject = "unknown";
        var role = "user";

        // Try to get from Authorization header
        if (HttpContext.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var token = authHeader.ToString();
            if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                token = token.Substring("Bearer ".Length).Trim();

                try
                {
                    var handler = new JwtSecurityTokenHandler();
                    var jwtToken = handler.ReadJwtToken(token);

                    subject = jwtToken.Claims.FirstOrDefault(x => x.Type == "sub")?.Value ?? "unknown";
                    role = jwtToken.Claims.FirstOrDefault(x => x.Type == "role")?.Value ?? "user";

                    Console.WriteLine($"Extracted from JWT - subject: {subject}, role: {role}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to parse JWT: {ex.Message}");
                }
            }
        }

        return (subject, role);
    }

    // GET api/books
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Book>>> Get([FromQuery] int limit = 10)
    {
        if (limit <= 0 || limit > 100) return BadRequest("The limit should been between [1-100]");

        try
        {
            var booksTask = bookRepository.GetBooksAsync(limit);

            // Wait with a 25-second timeout
            if (await Task.WhenAny(booksTask, Task.Delay(25000)) == booksTask)
            {
                var books = await booksTask;
                return Ok(books);
            }
            else
            {
                return StatusCode(503, new { message = "DynamoDB query timed out" });
            }
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    // GET api/books/5
    [HttpGet("{id}")]
    public async Task<ActionResult<Book>> Get(Guid id)
    {
        var result = await bookRepository.GetByIdAsync(id);
        if (result == null) return NotFound();
        return Ok(result);
    }

    // POST api/books
    [HttpPost]
    public async Task<ActionResult<Book>> Post([FromBody] Book book)
    {
        var (subject, role) = ExtractClaimsFromJwt();

        // Only admins can create books
        if (role != "admin")
            return StatusCode(403, new { message = "Access denied: Only admins can create books" });

        if (book == null) return ValidationProblem("Invalid input! Book not informed");

        try
        {
            var createTask = bookRepository.CreateAsync(book);
            if (await Task.WhenAny(createTask, Task.Delay(25000)) == createTask)
            {
                var result = await createTask;
                if (result)
                    return CreatedAtAction(nameof(Get), new { id = book.Id }, book);
                else
                    return BadRequest("Fail to persist");
            }
            else
                return StatusCode(503, new { message = "DynamoDB operation timed out" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    // PUT api/books/5
    [HttpPut("{id}")]
    public async Task<IActionResult> Put(Guid id, [FromBody] Book book)
    {
        var (subject, role) = ExtractClaimsFromJwt();

        // Only admins can update books
        if (role != "admin")
            return StatusCode(403, new { message = "Access denied: Only admins can update books" });

        if (id == Guid.Empty || book == null) return ValidationProblem("Invalid request payload");

        try
        {
            var getTask = bookRepository.GetByIdAsync(id);
            if (await Task.WhenAny(getTask, Task.Delay(25000)) != getTask)
                return StatusCode(503, new { message = "DynamoDB operation timed out" });

            var bookRetrieved = await getTask;
            if (bookRetrieved == null)
                return NotFound($"Invalid input! No book found with id:{id}");

            book.Id = bookRetrieved.Id;
            var updateTask = bookRepository.UpdateAsync(book);

            if (await Task.WhenAny(updateTask, Task.Delay(25000)) == updateTask)
            {
                await updateTask;
                return Ok();
            }
            else
                return StatusCode(503, new { message = "DynamoDB operation timed out" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    // DELETE api/books/5
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var (subject, role) = ExtractClaimsFromJwt();

        // Only admins can delete books
        if (role != "admin")
            return StatusCode(403, new { message = "Access denied: Only admins can delete books" });

        if (id == Guid.Empty) return ValidationProblem("Invalid request payload");

        try
        {
            var getTask = bookRepository.GetByIdAsync(id);
            if (await Task.WhenAny(getTask, Task.Delay(25000)) != getTask)
                return StatusCode(503, new { message = "DynamoDB operation timed out" });

            var bookRetrieved = await getTask;
            if (bookRetrieved == null)
                return NotFound($"Invalid input! No book found with id:{id}");

            var deleteTask = bookRepository.DeleteAsync(bookRetrieved);
            if (await Task.WhenAny(deleteTask, Task.Delay(25000)) == deleteTask)
            {
                await deleteTask;
                return Ok();
            }
            else
                return StatusCode(503, new { message = "DynamoDB operation timed out" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }
}
