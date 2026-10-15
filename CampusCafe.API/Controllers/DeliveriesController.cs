using Microsoft.AspNetCore.Mvc;
using System.Data.OleDb;

namespace CampusCafe.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DeliveriesController : ControllerBase
    {
        private readonly string connectionString;

        public DeliveriesController(IConfiguration configuration)
        {
            connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? DbHelper.FallbackConnection;
        }

        // GET api/Deliveries  -> history of all deliveries (newest first)
        [HttpGet]
        public IActionResult GetDeliveries()
        {
            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                try
                {
                    conn.Open();

                    var ids = new List<int>();
                    using (var cmd = new OleDbCommand("SELECT DeliveryID FROM [Delivery_Receipt] ORDER BY DeliveryID DESC", conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (reader[0] != DBNull.Value) ids.Add(Convert.ToInt32(reader[0]));
                        }
                    }

                    var result = new List<object>();
                    foreach (int id in ids)
                    {
                        var delivery = LoadDelivery(conn, id);
                        if (delivery != null) result.Add(delivery);
                    }
                    return Ok(result);
                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"Error reading deliveries: {ex.Message}");
                }
            }
        }

        // GET api/Deliveries/5  -> one delivery / receipt
        [HttpGet("{id:int}")]
        public IActionResult GetDelivery(int id)
        {
            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                try
                {
                    conn.Open();
                    var delivery = LoadDelivery(conn, id);
                    if (delivery == null) return NotFound("Delivery not found.");
                    return Ok(delivery);
                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"Error reading delivery: {ex.Message}");
                }
            }
        }

        // POST api/Deliveries
        // Saves, in ONE transaction:
        //   Delivery_Receipt (header) -> Delivery_Item (one row per ingredient) -> Delivery_Payment
        // and adds the received quantities (whole numbers) to Ingredient.CurrentQuantity,
        // so the stock in the .accdb is updated automatically.
        [HttpPost]
        public IActionResult CreateDelivery([FromBody] DeliveryCreateDto dto)
        {
            if (dto == null) return BadRequest("Invalid delivery data.");
            if (dto.SupplierID <= 0) return BadRequest("Please choose a supplier.");
            if (dto.Items == null || dto.Items.Count == 0) return BadRequest("Add at least one delivery item.");
            if (dto.Items.Any(i => (i.IngredientID <= 0 && string.IsNullOrWhiteSpace(i.IngredientName)) || i.Quantity <= 0))
                return BadRequest("Every delivery item needs an ingredient (chosen or typed) and a whole-number quantity of 1 or more.");
            if (dto.AmountPaid <= 0) return BadRequest("Please enter the payment amount.");

            string? method = DbHelper.NormalizeBusinessPaymentMethod(dto.PaymentMethod);
            if (method == null)
                return BadRequest("Unsupported payment method. Use Cash, Card, Bank Transfer or Check.");

            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                OleDbTransaction? tx = null;
                try
                {
                    conn.Open();

                    var receiptCols = DbHelper.GetColumns(conn, "Delivery_Receipt");
                    var itemCols = DbHelper.GetColumns(conn, "Delivery_Item");
                    var payCols = DbHelper.GetColumns(conn, "Delivery_Payment");
                    DbHelper.RequireTable(receiptCols, "Delivery_Receipt");
                    DbHelper.RequireTable(itemCols, "Delivery_Item");
                    DbHelper.RequireTable(payCols, "Delivery_Payment");

                    if (!DbHelper.Exists(conn, null, "SELECT COUNT(*) FROM Supplier WHERE SupplierID = ?", dto.SupplierID))
                        return BadRequest("Supplier not found.");

                    DateTime now = DbHelper.Now();
                    tx = conn.BeginTransaction();

                    // 1. Delivery_Receipt (parent row - the items and payment point to it)
                    int deliveryId = DbHelper.NextId(conn, tx, "Delivery_Receipt", "DeliveryID");
                    DbHelper.Insert(conn, tx, "Delivery_Receipt", receiptCols,
                        ("DeliveryID", deliveryId),
                        ("SupplierID", dto.SupplierID),
                        ("DeliveryDate", now));

                    // 2. Delivery_Item rows + raise the ingredient stock
                    int nextItemId = DbHelper.NextId(conn, tx, "Delivery_Item", "DeliveryItemID");
                    foreach (var item in dto.Items)
                    {
                        // an ingredient that is not in the list yet is created from the typed name
                        var (ingredientId, _) = DbHelper.ResolveIngredient(conn, tx, item.IngredientID, item.IngredientName, item.Unit);

                        DbHelper.Insert(conn, tx, "Delivery_Item", itemCols,
                            ("DeliveryItemID", nextItemId),
                            ("DeliveryID", deliveryId),
                            ("IngredientID", ingredientId),
                            ("QuantityReceived", item.Quantity));
                        nextItemId++;

                        DbHelper.NonQuery(conn, tx,
                            "UPDATE Ingredient SET CurrentQuantity = CurrentQuantity + ? WHERE IngredientID = ?",
                            item.Quantity, ingredientId);

                        // keep the supplier <-> ingredient link up to date
                        if (!DbHelper.Exists(conn, tx,
                                "SELECT COUNT(*) FROM Supplier_Ingredient WHERE SupplierID = ? AND IngredientID = ?",
                                dto.SupplierID, ingredientId))
                        {
                            var linkCols = DbHelper.GetColumns(conn, "Supplier_Ingredient");
                            if (linkCols.Count > 0)
                                DbHelper.Insert(conn, tx, "Supplier_Ingredient", linkCols,
                                    ("SupplierID", dto.SupplierID),
                                    ("IngredientID", ingredientId));
                        }
                    }

                    // 3. Delivery_Payment
                    int paymentId = DbHelper.NextId(conn, tx, "Delivery_Payment", "DeliveryPaymentID");
                    DbHelper.Insert(conn, tx, "Delivery_Payment", payCols,
                        ("DeliveryPaymentID", paymentId),
                        ("DeliveryID", deliveryId),
                        ("PaymentDate", now),
                        ("AmountPaid", dto.AmountPaid),
                        ("PaymentMethod", method));

                    tx.Commit();

                    var delivery = LoadDelivery(conn, deliveryId);
                    return Ok(delivery);
                }
                catch (InvalidOperationException ex)
                {
                    DbHelper.SafeRollback(tx);
                    return BadRequest(ex.Message);
                }
                catch (Exception ex)
                {
                    DbHelper.SafeRollback(tx);
                    return StatusCode(500, $"Failed to save delivery: {ex.Message}");
                }
            }
        }

        // POST api/Deliveries/5/payments   body: { "amountPaid": 250.00, "paymentMethod": "Card (Visa)" }
        // Records one more payment (Delivery_Payment row) on a delivery that already exists -
        // e.g. when a delivery is paid in several instalments.
        [HttpPost("{id:int}/payments")]
        public IActionResult AddPayment(int id, [FromBody] DeliveryPaymentDto dto)
        {
            if (dto == null) return BadRequest("Invalid payment data.");
            if (dto.AmountPaid <= 0) return BadRequest("Please enter the payment amount.");

            string? method = DbHelper.NormalizeBusinessPaymentMethod(dto.PaymentMethod);
            if (method == null)
                return BadRequest("Unsupported payment method. Use Cash, Card, Bank Transfer or Check.");

            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                OleDbTransaction? tx = null;
                try
                {
                    conn.Open();

                    var payCols = DbHelper.GetColumns(conn, "Delivery_Payment");
                    DbHelper.RequireTable(payCols, "Delivery_Payment");

                    if (!DbHelper.Exists(conn, null, "SELECT COUNT(*) FROM Delivery_Receipt WHERE DeliveryID = ?", id))
                        return NotFound("Delivery not found.");

                    tx = conn.BeginTransaction();
                    int paymentId = DbHelper.NextId(conn, tx, "Delivery_Payment", "DeliveryPaymentID");
                    DbHelper.Insert(conn, tx, "Delivery_Payment", payCols,
                        ("DeliveryPaymentID", paymentId),
                        ("DeliveryID", id),
                        ("PaymentDate", DbHelper.Now()),
                        ("AmountPaid", dto.AmountPaid),
                        ("PaymentMethod", method));
                    tx.Commit();

                    return Ok(LoadDelivery(conn, id));
                }
                catch (Exception ex)
                {
                    DbHelper.SafeRollback(tx);
                    return StatusCode(500, $"Failed to save payment: {ex.Message}");
                }
            }
        }

        // Builds the receipt-style object for one delivery (header + items + payments)
        private static object? LoadDelivery(OleDbConnection conn, int deliveryId)
        {
            int supplierId;
            string supplierName;
            string deliveryDate;

            string headerSql =
                "SELECT r.SupplierID, r.DeliveryDate, s.CompanyName " +
                "FROM [Delivery_Receipt] AS r INNER JOIN [Supplier] AS s ON r.SupplierID = s.SupplierID " +
                "WHERE r.DeliveryID = ?";

            using (var cmd = new OleDbCommand(headerSql, conn))
            {
                cmd.Parameters.Add(DbHelper.MakeParam(deliveryId));
                using (var rd = cmd.ExecuteReader())
                {
                    if (!rd.Read()) return null;
                    supplierId = Convert.ToInt32(rd["SupplierID"]);
                    deliveryDate = DbHelper.Fmt(rd["DeliveryDate"]);
                    supplierName = rd["CompanyName"]?.ToString() ?? "";
                }
            }

            var items = new List<object>();
            string itemSql =
                "SELECT i.DeliveryItemID, i.IngredientID, g.IngredientName, g.UnitOfMeasurement, i.QuantityReceived, g.CurrentQuantity " +
                "FROM [Delivery_Item] AS i INNER JOIN [Ingredient] AS g ON i.IngredientID = g.IngredientID " +
                "WHERE i.DeliveryID = ? ORDER BY i.DeliveryItemID";
            using (var cmd = new OleDbCommand(itemSql, conn))
            {
                cmd.Parameters.Add(DbHelper.MakeParam(deliveryId));
                using (var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                    {
                        items.Add(new
                        {
                            deliveryItemID = Convert.ToInt32(rd["DeliveryItemID"]),
                            ingredientID = Convert.ToInt32(rd["IngredientID"]),
                            ingredientName = rd["IngredientName"]?.ToString() ?? "",
                            unit = rd["UnitOfMeasurement"]?.ToString() ?? "",
                            quantityReceived = DbHelper.Whole(rd["QuantityReceived"]),
                            currentStock = DbHelper.Whole(rd["CurrentQuantity"])
                        });
                    }
                }
            }

            var payments = new List<object>();
            decimal totalPaid = 0m;
            string paySql =
                "SELECT DeliveryPaymentID, PaymentDate, AmountPaid, PaymentMethod " +
                "FROM [Delivery_Payment] WHERE DeliveryID = ? ORDER BY DeliveryPaymentID";
            using (var cmd = new OleDbCommand(paySql, conn))
            {
                cmd.Parameters.Add(DbHelper.MakeParam(deliveryId));
                using (var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                    {
                        decimal amount = DbHelper.Dec(rd["AmountPaid"]);
                        totalPaid += amount;
                        payments.Add(new
                        {
                            deliveryPaymentID = Convert.ToInt32(rd["DeliveryPaymentID"]),
                            paymentDate = DbHelper.Fmt(rd["PaymentDate"]),
                            amountPaid = amount,
                            paymentMethod = rd["PaymentMethod"]?.ToString() ?? ""
                        });
                    }
                }
            }

            return new
            {
                deliveryID = deliveryId,
                receiptNumber = "DR-" + deliveryId.ToString("D5"),
                supplierID = supplierId,
                supplierName,
                deliveryDate,
                items,
                payments,
                totalPaid
            };
        }
    }

    public class DeliveryCreateDto
    {
        public int SupplierID { get; set; }
        public List<DeliveryItemDto> Items { get; set; } = new List<DeliveryItemDto>();
        public decimal AmountPaid { get; set; }
        public string PaymentMethod { get; set; } = "Cash";
    }

    public class DeliveryItemDto
    {
        public int IngredientID { get; set; }          // 0 when the ingredient name was typed in
        public string IngredientName { get; set; } = string.Empty;  // typed name (custom ingredient)
        public string Unit { get; set; } = "pcs";       // unit for a brand-new typed ingredient
        public int Quantity { get; set; }   // whole numbers only
    }

    public class DeliveryPaymentDto
    {
        public decimal AmountPaid { get; set; }
        public string PaymentMethod { get; set; } = "Cash";
    }
}
