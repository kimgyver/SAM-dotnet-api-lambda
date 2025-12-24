using Amazon.DynamoDBv2.DataModel;
using Microsoft.AspNetCore.Mvc;
using ServerlessAPI.Entities;
using ServerlessAPI.Repositories;

namespace ServerlessAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class BooksController : ControllerBase
{
    private readonly IBookRepository bookRepository;
    private readonly JwtValidator jwtValidator;

    public BooksController(IBookRepository bookRepository, JwtValidator jwtValidator)
    {
        this.bookRepository = bookRepository;
        this.jwtValidator = jwtValidator;
    }

    private bool ValidateJwt(out string subject)
    {
        if (!jwtValidator.ValidateJwtToken(HttpContext, out subject))
        {
            Console.WriteLine("Unauthorized access attempt");
            Console.Out.Flush();
            return false;
        }
        return true;
    }

    // GET api/books
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Book>>> Get([FromQuery] int limit = 10)
    {
        Console.WriteLine("=== GET /api/books called ===");
        Console.Out.Flush();

        // Validate JWT
        if (!ValidateJwt(out var subject))
        {
            Console.WriteLine("JWT validation failed");
            Console.Out.Flush();
            return Unauthorized(new { message = "Invalid or missing JWT token", error = "Unauthorized" });
        }

        Console.WriteLine($"JWT validated for user: {subject}");
        Console.WriteLine($"Limit parameter: {limit}");
        Console.Out.Flush();

        if (limit <= 0 || limit > 100) return BadRequest("The limit should been between [1-100]");

        Console.WriteLine($"Fetching books with limit: {limit}");
        Console.Out.Flush();

        try
        {
            Console.WriteLine("Starting GetBooksAsync call...");
            Console.Out.Flush();

            var booksTask = bookRepository.GetBooksAsync(limit);

            // Wait with a 25-second timeout
            if (await Task.WhenAny(booksTask, Task.Delay(25000)) == booksTask)
            {
                var books = await booksTask;
                Console.WriteLine($"Got {books.Count()} books from repository");
                Console.Out.Flush();

                Console.WriteLine($"User {subject} requested books with limit {limit}");
                Console.Out.Flush();
                return Ok(books);
            }
            else
            {
                Console.WriteLine("ERROR: GetBooksAsync timed out after 25 seconds!");
                Console.Out.Flush();
                Console.WriteLine("GetBooksAsync timed out");
                Console.Out.Flush();
                return StatusCode(503, new { message = "DynamoDB query timed out" });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
            Console.Out.Flush();
            Console.WriteLine("Error fetching books");
            Console.Out.Flush();
            return StatusCode(500, new { message = ex.Message });
        }
    }

    // GET api/books/5
    [HttpGet("{id}")]
    public async Task<ActionResult<Book>> Get(Guid id)
    {
        // Validate JWT
        if (!ValidateJwt(out var subject))
        {
            return Unauthorized(new { message = "Invalid or missing JWT token", error = "Unauthorized" });
        }

        var result = await bookRepository.GetByIdAsync(id);

        if (result == null)
        {
            return NotFound();
        }

        Console.WriteLine($"User {subject} retrieved book {id}");
        Console.Out.Flush();
        return Ok(result);
    }

    // POST api/books
    [HttpPost]
    public async Task<ActionResult<Book>> Post([FromBody] Book book)
    {
        Console.WriteLine("=== POST /api/books called ===");
        // Validate JWT
        if (!ValidateJwt(out var subject))
        {
            Console.WriteLine("JWT validation failed");
            return Unauthorized(new { message = "Invalid or missing JWT token", error = "Unauthorized" });
        }

        Console.WriteLine($"JWT validated for user: {subject}");
        if (book == null) return ValidationProblem("Invalid input! Book not informed");

        try
        {
            Console.WriteLine("Starting CreateAsync call...");
            var createTask = bookRepository.CreateAsync(book);

            if (await Task.WhenAny(createTask, Task.Delay(25000)) == createTask)
            {
                var result = await createTask;
                if (result)
                {
                    Console.WriteLine($"Book {book.Id} created successfully");
                    Console.WriteLine($"User {subject} created book {book.Id}");
                    Console.Out.Flush();
                    return CreatedAtAction(
                        nameof(Get),
                        new { id = book.Id },
                        book);
                }
                else
                {
                    Console.WriteLine("Failed to persist book");
                    return BadRequest("Fail to persist");
                }
            }
            else
            {
                Console.WriteLine("ERROR: CreateAsync timed out after 25 seconds!");
                Console.WriteLine("CreateAsync timed out");
                Console.Out.Flush();
                return StatusCode(503, new { message = "DynamoDB operation timed out" });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
            Console.WriteLine("Error creating book");
            Console.Out.Flush();
            return StatusCode(500, new { message = ex.Message });
        }
    }

    // PUT api/books/5
    [HttpPut("{id}")]
    public async Task<IActionResult> Put(Guid id, [FromBody] Book book)
    {
        Console.WriteLine($"=== PUT /api/books/{id} called ===");
        // Validate JWT
        if (!ValidateJwt(out var subject))
        {
            Console.WriteLine("JWT validation failed");
            return Unauthorized(new { message = "Invalid or missing JWT token", error = "Unauthorized" });
        }

        Console.WriteLine($"JWT validated for user: {subject}");
        if (id == Guid.Empty || book == null) return ValidationProblem("Invalid request payload");

        try
        {
            Console.WriteLine($"Retrieving book with id: {id}");
            var getTask = bookRepository.GetByIdAsync(id);

            if (await Task.WhenAny(getTask, Task.Delay(25000)) != getTask)
            {
                Console.WriteLine("ERROR: GetByIdAsync timed out!");
                return StatusCode(503, new { message = "DynamoDB operation timed out" });
            }

            var bookRetrieved = await getTask;

            if (bookRetrieved == null)
            {
                var errorMsg = $"Invalid input! No book found with id:{id}";
                Console.WriteLine(errorMsg);
                return NotFound(errorMsg);
            }

            book.Id = bookRetrieved.Id;

            Console.WriteLine("Starting UpdateAsync call...");
            var updateTask = bookRepository.UpdateAsync(book);

            if (await Task.WhenAny(updateTask, Task.Delay(25000)) == updateTask)
            {
                await updateTask;
                Console.WriteLine($"Book {id} updated successfully");
                Console.WriteLine($"User {subject} updated book {id}");
                Console.Out.Flush();
                return Ok();
            }
            else
            {
                Console.WriteLine("ERROR: UpdateAsync timed out!");
                return StatusCode(503, new { message = "DynamoDB operation timed out" });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
            Console.WriteLine("Error updating book");
            Console.Out.Flush();
            return StatusCode(500, new { message = ex.Message });
        }
    }

    // DELETE api/books/5
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        Console.WriteLine($"=== DELETE /api/books/{id} called ===");
        // Validate JWT
        if (!ValidateJwt(out var subject))
        {
            Console.WriteLine("JWT validation failed");
            return Unauthorized(new { message = "Invalid or missing JWT token", error = "Unauthorized" });
        }

        Console.WriteLine($"JWT validated for user: {subject}");
        if (id == Guid.Empty) return ValidationProblem("Invalid request payload");

        try
        {
            Console.WriteLine($"Retrieving book with id: {id}");
            var getTask = bookRepository.GetByIdAsync(id);

            if (await Task.WhenAny(getTask, Task.Delay(25000)) != getTask)
            {
                Console.WriteLine("ERROR: GetByIdAsync timed out!");
                return StatusCode(503, new { message = "DynamoDB operation timed out" });
            }

            var bookRetrieved = await getTask;

            if (bookRetrieved == null)
            {
                var errorMsg = $"Invalid input! No book found with id:{id}";
                Console.WriteLine(errorMsg);
                return NotFound(errorMsg);
            }

            Console.WriteLine("Starting DeleteAsync call...");
            var deleteTask = bookRepository.DeleteAsync(bookRetrieved);

            if (await Task.WhenAny(deleteTask, Task.Delay(25000)) == deleteTask)
            {
                await deleteTask;
                Console.WriteLine($"Book {id} deleted successfully");
                Console.WriteLine($"User {subject} deleted book {id}");
                Console.Out.Flush();
                return Ok();
            }
            else
            {
                Console.WriteLine("ERROR: DeleteAsync timed out!");
                return StatusCode(503, new { message = "DynamoDB operation timed out" });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
            Console.WriteLine("Error deleting book");
            Console.Out.Flush();
            return StatusCode(500, new { message = ex.Message });
        }
    }
}
