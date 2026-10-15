using Microsoft.AspNetCore.Mvc;
using System.Data.OleDb;

namespace CampusCafe.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class IngredientsController : ControllerBase
    {
        private readonly string connectionString;

        public IngredientsController(IConfiguration configuration)
        {
            connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? DbHelper.FallbackConnection;
        }

        // GET api/Ingredients
        [HttpGet]
        public IActionResult GetIngredients()
        {
            var ingredients = new List<object>();

            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                try
                {
                    conn.Open();
                    string query = "SELECT IngredientID, IngredientName, UnitOfMeasurement, CurrentQuantity FROM Ingredient ORDER BY IngredientID";
                    using (OleDbCommand cmd = new OleDbCommand(query, conn))
                    using (OleDbDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int qty = DbHelper.Whole(reader["CurrentQuantity"]);
                            ingredients.Add(new
                            {
                                IngredientID = reader["IngredientID"] != DBNull.Value ? Convert.ToInt32(reader["IngredientID"]) : 0,
                                IngredientName = reader["IngredientName"]?.ToString() ?? "",
                                Unit = reader["UnitOfMeasurement"]?.ToString() ?? "pcs",
                                StockQuantity = qty,
                                // 0 = Unavailable, 1-9 = Low stock, 10 and above = Available
                                Status = DbHelper.StockStatus(qty)
                            });
                        }
                    }
                    return Ok(ingredients);
                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"Error reading ingredients: {ex.Message}");
                }
            }
        }

        // POST api/Ingredients  -> add a brand new ingredient (the "+" form)
        [HttpPost]
        public IActionResult AddIngredient([FromBody] IngredientCreateDto item)
        {
            if (item == null) return BadRequest("Invalid ingredient data.");

            string name = (item.IngredientName ?? "").Trim();
            string unit = string.IsNullOrWhiteSpace(item.Unit) ? "pcs" : item.Unit.Trim();

            if (name.Length == 0) return BadRequest("Ingredient name is required.");
            if (name.Length > 100) return BadRequest("Ingredient name is too long (max 100 characters).");
            if (unit.Length > 30) return BadRequest("Unit is too long (max 30 characters).");
            if (item.StockQuantity < 0) return BadRequest("Starting quantity cannot be negative.");

            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                OleDbTransaction? tx = null;
                try
                {
                    conn.Open();

                    if (DbHelper.Exists(conn, null, "SELECT COUNT(*) FROM Ingredient WHERE IngredientName = ?", name))
                        return Conflict($"'{name}' is already in the ingredient list. Use the + button to add stock instead.");

                    tx = conn.BeginTransaction();
                    int nextId = DbHelper.NextId(conn, tx, "Ingredient", "IngredientID");

                    DbHelper.NonQuery(conn, tx,
                        "INSERT INTO Ingredient (IngredientID, IngredientName, UnitOfMeasurement, CurrentQuantity, MinimumStockLevel) VALUES (?, ?, ?, ?, ?)",
                        nextId, name, unit, item.StockQuantity, 10);
                    tx.Commit();

                    return Ok(new
                    {
                        message = "Ingredient saved successfully!",
                        ingredientID = nextId,
                        ingredientName = name,
                        unit,
                        stockQuantity = item.StockQuantity
                    });
                }
                catch (Exception ex)
                {
                    DbHelper.SafeRollback(tx);
                    return StatusCode(500, $"Failed to insert ingredient: {ex.Message}");
                }
            }
        }

        // PUT api/Ingredients/5/update-stock  -> set the stock to an exact number (kept for compatibility)
        [HttpPut("{id:int}/update-stock")]
        public IActionResult UpdateStock(int id, [FromBody] StockUpdateDto dto)
        {
            if (dto == null) return BadRequest("Invalid stock data.");
            if (dto.NewQuantity < 0) return BadRequest("Stock cannot be negative.");

            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                try
                {
                    conn.Open();
                    int rows = DbHelper.NonQuery(conn, null,
                        "UPDATE Ingredient SET CurrentQuantity = ? WHERE IngredientID = ?", dto.NewQuantity, id);
                    if (rows == 0) return NotFound("Ingredient not found.");
                    return Ok(new { message = "Stock updated successfully!", newQuantity = dto.NewQuantity });
                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"Failed to update stock: {ex.Message}");
                }
            }
        }

        // PUT api/Ingredients/5/adjust   body: { "delta": 3 } or { "delta": -2 }
        // The "+" / "-" buttons. The change is applied inside the database
        // (CurrentQuantity = CurrentQuantity + delta) so two people clicking at once never overwrite each other.
        [HttpPut("{id:int}/adjust")]
        public IActionResult AdjustStock(int id, [FromBody] StockAdjustDto dto)
        {
            if (dto == null || dto.Delta == 0) return BadRequest("Enter a whole number of 1 or more.");

            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                try
                {
                    conn.Open();

                    int rows = DbHelper.NonQuery(conn, null,
                        "UPDATE Ingredient SET CurrentQuantity = CurrentQuantity + ? WHERE IngredientID = ? AND CurrentQuantity + ? >= 0",
                        dto.Delta, id, dto.Delta);

                    if (rows == 0)
                    {
                        if (!DbHelper.Exists(conn, null, "SELECT COUNT(*) FROM Ingredient WHERE IngredientID = ?", id))
                            return NotFound("Ingredient not found.");
                        return BadRequest("Not enough stock - the quantity cannot go below 0.");
                    }

                    object? now = DbHelper.Scalar(conn, null, "SELECT CurrentQuantity FROM Ingredient WHERE IngredientID = ?", id);
                    return Ok(new { ingredientID = id, newQuantity = DbHelper.Whole(now) });
                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"Failed to adjust stock: {ex.Message}");
                }
            }
        }

        // DELETE api/Ingredients/5  -> the "remove" button
        [HttpDelete("{id:int}")]
        public IActionResult DeleteIngredient(int id)
        {
            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                OleDbTransaction? tx = null;
                try
                {
                    conn.Open();

                    if (!DbHelper.Exists(conn, null, "SELECT COUNT(*) FROM Ingredient WHERE IngredientID = ?", id))
                        return NotFound("Ingredient not found.");

                    // Delivery history points at the ingredient, so it cannot be removed once it was delivered.
                    if (DbHelper.Exists(conn, null, "SELECT COUNT(*) FROM Delivery_Item WHERE IngredientID = ?", id))
                        return Conflict("This ingredient appears in delivery receipts, so it cannot be removed. Set its stock to 0 with the - button instead.");

                    tx = conn.BeginTransaction();
                    DbHelper.NonQuery(conn, tx, "DELETE FROM Supplier_Ingredient WHERE IngredientID = ?", id);
                    DbHelper.NonQuery(conn, tx, "DELETE FROM Ingredient WHERE IngredientID = ?", id);
                    tx.Commit();

                    return Ok(new { message = "Ingredient removed." });
                }
                catch (Exception ex)
                {
                    DbHelper.SafeRollback(tx);
                    return StatusCode(500, $"Failed to remove ingredient: {ex.Message}");
                }
            }
        }
    }

    public class IngredientCreateDto
    {
        public string IngredientName { get; set; } = string.Empty;
        public int StockQuantity { get; set; }
        public string Unit { get; set; } = "pcs";
    }

    public class StockUpdateDto
    {
        public int NewQuantity { get; set; }
    }

    public class StockAdjustDto
    {
        public int Delta { get; set; }
    }
}
