using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Data.OleDb;
using Microsoft.Extensions.Configuration;

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
                ?? @"Provider=Microsoft.ACE.OLEDB.12.0;Data Source=C:\Users\alcan\Downloads\Physical Data Model (GILO, PAMATIAN, VILLAFOR, VELASCO).accdb;";
        }

        [HttpGet]
        public IActionResult GetIngredients()
        {
            var ingredients = new List<object>();

            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                try
                {
                    conn.Open();
                    string query = "SELECT IngredientID, IngredientName, UnitOfMeasurement, CurrentQuantity FROM Ingredient";
                    using (OleDbCommand cmd = new OleDbCommand(query, conn))
                    {
                        using (OleDbDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                ingredients.Add(new
                                {
                                    IngredientID = reader["IngredientID"] != DBNull.Value ? Convert.ToInt32(reader["IngredientID"]) : 0,
                                    IngredientName = reader["IngredientName"]?.ToString() ?? "",
                                    Unit = reader["UnitOfMeasurement"]?.ToString() ?? "pcs",
                                    StockQuantity = reader["CurrentQuantity"] != DBNull.Value ? Convert.ToInt32(reader["CurrentQuantity"]) : 0
                                });
                            }
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

        [HttpPost]
        public IActionResult AddIngredient([FromBody] IngredientCreateDto item)
        {
            if (item == null) return BadRequest("Invalid ingredient data.");

            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                try
                {
                    conn.Open();
                    int nextId = 1;
                    using (OleDbCommand maxCmd = new OleDbCommand("SELECT MAX(IngredientID) FROM Ingredient", conn))
                    {
                        object res = maxCmd.ExecuteScalar();
                        if (res != null && res != DBNull.Value) nextId = Convert.ToInt32(res) + 1;
                    }

                    string query = "INSERT INTO Ingredient (IngredientID, IngredientName, UnitOfMeasurement, CurrentQuantity, MinimumStockLevel) VALUES (?, ?, ?, ?, ?)";
                    using (OleDbCommand cmd = new OleDbCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("?", nextId);
                        cmd.Parameters.AddWithValue("?", item.IngredientName ?? "");
                        cmd.Parameters.AddWithValue("?", item.Unit ?? "pcs");
                        cmd.Parameters.AddWithValue("?", item.StockQuantity);
                        cmd.Parameters.AddWithValue("?", 1);
                        cmd.ExecuteNonQuery();
                    }

                    return Ok(new { message = "Ingredient saved successfully!" });
                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"Failed to insert ingredient: {ex.Message}");
                }
            }
        }

        [HttpPut("{id}/update-stock")]
        public IActionResult UpdateStock(int id, [FromBody] StockUpdateDto dto)
        {
            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                try
                {
                    conn.Open();
                    string query = "UPDATE Ingredient SET CurrentQuantity = ? WHERE IngredientID = ?";
                    using (OleDbCommand cmd = new OleDbCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("?", dto.NewQuantity);
                        cmd.Parameters.AddWithValue("?", id);
                        cmd.ExecuteNonQuery();
                    }
                    return Ok(new { message = "Stock updated successfully!" });
                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"Failed to update stock: {ex.Message}");
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
}