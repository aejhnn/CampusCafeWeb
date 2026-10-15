using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Data.OleDb;
using Microsoft.Extensions.Configuration;

namespace CampusCafe.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SuppliersController : ControllerBase
    {
        private readonly string connectionString;

        public SuppliersController(IConfiguration configuration)
        {
            connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? @"Provider=Microsoft.ACE.OLEDB.12.0;Data Source=C:\Users\alcan\Downloads\Physical Data Model (GILO, PAMATIAN, VILLAFOR, VELASCO).accdb;";
        }

        [HttpGet]
        public IActionResult GetSuppliers()
        {
            var suppliers = new List<object>();

            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                try
                {
                    conn.Open();
                    string query = "SELECT SupplierID, CompanyName, ContactPerson, TelephoneNumber FROM Supplier";
                    using (OleDbCommand cmd = new OleDbCommand(query, conn))
                    {
                        using (OleDbDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                suppliers.Add(new
                                {
                                    SupplierID = reader["SupplierID"] != DBNull.Value ? Convert.ToInt32(reader["SupplierID"]) : 0,
                                    SupplierName = reader["CompanyName"]?.ToString() ?? "",
                                    ContactPerson = reader["ContactPerson"]?.ToString() ?? "",
                                    Phone = reader["TelephoneNumber"]?.ToString() ?? ""
                                });
                            }
                        }
                    }
                    return Ok(suppliers);
                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"Error reading suppliers: {ex.Message}");
                }
            }
        }

        [HttpPost]
        public IActionResult AddSupplier([FromBody] SupplierCreateDto sup)
        {
            if (sup == null) return BadRequest("Invalid supplier data.");

            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                try
                {
                    conn.Open();
                    int nextId = 1;
                    using (OleDbCommand maxCmd = new OleDbCommand("SELECT MAX(SupplierID) FROM Supplier", conn))
                    {
                        object res = maxCmd.ExecuteScalar();
                        if (res != null && res != DBNull.Value) nextId = Convert.ToInt32(res) + 1;
                    }

                    string query = "INSERT INTO Supplier (SupplierID, CompanyName, ContactPerson, TelephoneNumber) VALUES (?, ?, ?, ?)";
                    using (OleDbCommand cmd = new OleDbCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("?", nextId);
                        cmd.Parameters.AddWithValue("?", sup.SupplierName ?? "");
                        cmd.Parameters.AddWithValue("?", sup.ContactPerson ?? "");
                        cmd.Parameters.AddWithValue("?", sup.Phone ?? "");
                        cmd.ExecuteNonQuery();
                    }

                    return Ok(new { message = "Supplier saved successfully!" });
                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"Failed to insert supplier: {ex.Message}");
                }
            }
        }

        // GET api/Suppliers/3/ingredients -> ingredients this supplier provides (Supplier_Ingredient)
        [HttpGet("{id:int}/ingredients")]
        public IActionResult GetSupplierIngredients(int id)
        {
            var list = new List<object>();
            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                try
                {
                    conn.Open();
                    string sql =
                        "SELECT g.IngredientID, g.IngredientName, g.UnitOfMeasurement " +
                        "FROM [Supplier_Ingredient] AS si INNER JOIN [Ingredient] AS g ON si.IngredientID = g.IngredientID " +
                        "WHERE si.SupplierID = ? ORDER BY g.IngredientName";
                    using (OleDbCommand cmd = new OleDbCommand(sql, conn))
                    {
                        cmd.Parameters.Add(DbHelper.MakeParam(id));
                        using (OleDbDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                list.Add(new
                                {
                                    ingredientID = Convert.ToInt32(reader["IngredientID"]),
                                    ingredientName = reader["IngredientName"]?.ToString() ?? "",
                                    unit = reader["UnitOfMeasurement"]?.ToString() ?? ""
                                });
                            }
                        }
                    }
                    return Ok(list);
                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"Error reading supplier ingredients: {ex.Message}");
                }
            }
        }

        // GET api/Suppliers/supply-history -> all supplier receipts (newest first)
        [HttpGet("supply-history")]
        public IActionResult GetSupplyHistory()
        {
            var list = new List<object>();
            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                try
                {
                    conn.Open();
                    string sql =
                        "SELECT r.SupplierReceiptID, r.SupplierID, r.ReceiptNumber, r.ReceiptDate, r.TotalAmount, s.CompanyName " +
                        "FROM [Supplier_Receipt] AS r INNER JOIN [Supplier] AS s ON r.SupplierID = s.SupplierID " +
                        "ORDER BY r.SupplierReceiptID DESC";
                    using (OleDbCommand cmd = new OleDbCommand(sql, conn))
                    using (OleDbDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(new
                            {
                                supplierReceiptID = Convert.ToInt32(reader["SupplierReceiptID"]),
                                supplierID = Convert.ToInt32(reader["SupplierID"]),
                                supplierName = reader["CompanyName"]?.ToString() ?? "",
                                receiptNumber = reader["ReceiptNumber"]?.ToString() ?? "",
                                receiptDate = DbHelper.Fmt(reader["ReceiptDate"]),
                                totalAmount = DbHelper.Dec(reader["TotalAmount"])
                            });
                        }
                    }
                    return Ok(list);
                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"Error reading supplier receipts: {ex.Message}");
                }
            }
        }

        // POST api/Suppliers/supply
        // Choose supplier + ingredient, enter the payment, get a receipt.
        // Saves in ONE transaction:
        //   Supplier_Ingredient (link, only if not yet linked) -> Supplier_Payment -> Supplier_Receipt
        [HttpPost("supply")]
        public IActionResult CreateSupply([FromBody] SupplyCreateDto dto)
        {
            if (dto == null) return BadRequest("Invalid supply data.");
            if (dto.SupplierID <= 0) return BadRequest("Please choose a supplier.");
            if (dto.IngredientID <= 0 && string.IsNullOrWhiteSpace(dto.IngredientName))
                return BadRequest("Please choose an ingredient or type its name.");
            if (dto.AmountPaid <= 0) return BadRequest("Please enter the payment amount.");

            string method = string.IsNullOrWhiteSpace(dto.PaymentMethod) ? "Cash" : dto.PaymentMethod.Trim();
            if (method.Length > 20) method = method.Substring(0, 20);

            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                OleDbTransaction? tx = null;
                try
                {
                    conn.Open();

                    var linkCols = DbHelper.GetColumns(conn, "Supplier_Ingredient");
                    var payCols = DbHelper.GetColumns(conn, "Supplier_Payment");
                    var receiptCols = DbHelper.GetColumns(conn, "Supplier_Receipt");
                    DbHelper.RequireTable(linkCols, "Supplier_Ingredient");
                    DbHelper.RequireTable(payCols, "Supplier_Payment");
                    DbHelper.RequireTable(receiptCols, "Supplier_Receipt");

                    object? supplierName = DbHelper.Scalar(conn, null, "SELECT CompanyName FROM Supplier WHERE SupplierID = ?", dto.SupplierID);
                    if (supplierName == null || supplierName == DBNull.Value) return BadRequest("Supplier not found.");

                    DateTime now = DbHelper.Now();
                    tx = conn.BeginTransaction();

                    // 0. the ingredient: pick an existing one, or create it from the typed name
                    var (ingredientId, ingredientCreated) =
                        DbHelper.ResolveIngredient(conn, tx, dto.IngredientID, dto.IngredientName, dto.Unit);
                    object? ingredientName = DbHelper.Scalar(conn, tx, "SELECT IngredientName FROM Ingredient WHERE IngredientID = ?", ingredientId);
                    object? unit = DbHelper.Scalar(conn, tx, "SELECT UnitOfMeasurement FROM Ingredient WHERE IngredientID = ?", ingredientId);

                    // 1. link the supplier to the ingredient (composite key - never insert a duplicate)
                    bool alreadyLinked = DbHelper.Exists(conn, tx,
                        "SELECT COUNT(*) FROM Supplier_Ingredient WHERE SupplierID = ? AND IngredientID = ?",
                        dto.SupplierID, ingredientId);
                    if (!alreadyLinked)
                    {
                        DbHelper.Insert(conn, tx, "Supplier_Ingredient", linkCols,
                            ("SupplierID", dto.SupplierID),
                            ("IngredientID", ingredientId));
                    }

                    // 2. payment
                    int paymentId = DbHelper.NextId(conn, tx, "Supplier_Payment", "SupplierPaymentID");
                    DbHelper.Insert(conn, tx, "Supplier_Payment", payCols,
                        ("SupplierPaymentID", paymentId),
                        ("SupplierID", dto.SupplierID),
                        ("PaymentDate", now),
                        ("AmountPaid", dto.AmountPaid),
                        ("PaymentMethod", method));

                    // 3. receipt (only after the payment row was saved)
                    int receiptId = DbHelper.NextId(conn, tx, "Supplier_Receipt", "SupplierReceiptID");
                    string receiptNumber = $"SR-{now:yyyyMMdd}-{receiptId:D5}";
                    DbHelper.Insert(conn, tx, "Supplier_Receipt", receiptCols,
                        ("SupplierReceiptID", receiptId),
                        ("SupplierID", dto.SupplierID),
                        ("ReceiptNumber", receiptNumber),
                        ("ReceiptDate", now),
                        ("TotalAmount", dto.AmountPaid));

                    tx.Commit();

                    return Ok(new
                    {
                        message = "Supplier payment saved and receipt created!",
                        supplierReceiptID = receiptId,
                        supplierPaymentID = paymentId,
                        receiptNumber,
                        receiptDate = DbHelper.Fmt(now),
                        supplierID = dto.SupplierID,
                        supplierName = supplierName.ToString(),
                        ingredientID = ingredientId,
                        ingredientCreated,
                        ingredientName = ingredientName?.ToString() ?? "",
                        unit = unit?.ToString() ?? "",
                        paymentMethod = method,
                        amountPaid = dto.AmountPaid,
                        newlyLinked = !alreadyLinked
                    });
                }
                catch (InvalidOperationException ex)
                {
                    DbHelper.SafeRollback(tx);
                    return BadRequest(ex.Message);
                }
                catch (Exception ex)
                {
                    DbHelper.SafeRollback(tx);
                    return StatusCode(500, $"Failed to save supplier payment: {ex.Message}");
                }
            }
        }
    }

    public class SupplierCreateDto
    {
        public string SupplierName { get; set; } = string.Empty;
        public string ContactPerson { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
    }

    public class SupplyCreateDto
    {
        public int SupplierID { get; set; }
        public int IngredientID { get; set; }          // 0 when the ingredient name was typed in
        public string IngredientName { get; set; } = string.Empty;  // typed name (custom ingredient)
        public string Unit { get; set; } = "pcs";       // unit for a brand-new typed ingredient
        public decimal AmountPaid { get; set; }
        public string PaymentMethod { get; set; } = "Cash";
    }
}
