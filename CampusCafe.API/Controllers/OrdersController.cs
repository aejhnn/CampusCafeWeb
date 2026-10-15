using Microsoft.AspNetCore.Mvc;
using System.Data.OleDb;

namespace CampusCafe.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ControllerBase
    {
        private readonly string connectionString;

        public OrdersController(IConfiguration configuration)
        {
            connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? DbHelper.FallbackConnection;
        }

        private sealed record OrderLine(int ProductID, string Name, int Quantity, decimal UnitPrice);

        /// <summary>
        /// Places an order, records the payment and creates the receipt.
        /// Tables written (all in ONE transaction - all saved or nothing saved):
        /// Customer (if new) -> Order -> Order_Item -> Payment -> Order_Payment -> Order_Receipt
        /// </summary>
        [HttpPost]
        public IActionResult PlaceOrder([FromBody] OrderCreateDto orderDto)
        {
            if (orderDto == null || orderDto.Items == null || orderDto.Items.Count == 0)
                return BadRequest("Order items cannot be empty.");

            if (string.IsNullOrWhiteSpace(orderDto.CustomerName))
                return BadRequest("Customer name is required.");

            // University ID: optional for guests, required for students / faculty
            string universityId = (orderDto.UniversityID ?? "").Trim();
            bool isGuest = string.Equals((orderDto.CustomerType ?? "").Trim(), "Guest", StringComparison.OrdinalIgnoreCase);
            if (isGuest) universityId = "";   // guests / visitors have no University ID
            if (universityId.Length > 30)
                return BadRequest("University ID is too long (max 30 characters).");
            if (!isGuest && universityId.Length == 0)
                return BadRequest("University ID is required for students and faculty (only guests can skip it).");
            if (!isGuest && !universityId.All(char.IsAsciiDigit))
                return BadRequest("University ID can only contain numbers.");

            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                OleDbTransaction? tx = null;
                try
                {
                    conn.Open();

                    // ---- read real table layouts BEFORE the transaction starts ----
                    var custCols = DbHelper.GetColumns(conn, "Customer");
                    var orderCols = DbHelper.GetColumns(conn, "Order");
                    var itemCols = DbHelper.GetColumns(conn, "Order_Item");
                    var payCols = DbHelper.GetColumns(conn, "Payment");
                    var orderPayCols = DbHelper.GetColumns(conn, "Order_Payment");
                    var receiptCols = DbHelper.GetColumns(conn, "Order_Receipt");

                    DbHelper.RequireTable(custCols, "Customer");
                    DbHelper.RequireTable(orderCols, "Order");
                    DbHelper.RequireTable(itemCols, "Order_Item");
                    DbHelper.RequireTable(payCols, "Payment");
                    DbHelper.RequireTable(orderPayCols, "Order_Payment");
                    DbHelper.RequireTable(receiptCols, "Order_Receipt");

                    // ---- price every item from the database (never trust the browser's prices) ----
                    var lines = new List<OrderLine>();
                    decimal subtotal = 0m;
                    foreach (var item in orderDto.Items)
                    {
                        if (item.Quantity <= 0)
                            return BadRequest("Each item quantity must be at least 1.");

                        object? priceObj = DbHelper.Scalar(conn, null, "SELECT Price FROM Product WHERE ProductID = ?", item.ProductID);
                        if (priceObj == null || priceObj == DBNull.Value)
                            return BadRequest($"Product #{item.ProductID} was not found.");

                        object? nameObj = DbHelper.Scalar(conn, null, "SELECT ProductName FROM Product WHERE ProductID = ?", item.ProductID);
                        string productName = nameObj?.ToString() ?? ("Product #" + item.ProductID);

                        decimal unitPrice = Convert.ToDecimal(priceObj);
                        lines.Add(new OrderLine(item.ProductID, productName, item.Quantity, unitPrice));
                        subtotal += unitPrice * item.Quantity;
                    }

                    decimal tax = Math.Round(subtotal * 0.12m, 2);
                    decimal total = subtotal + tax;

                    // ---- payment rules ----
                    // Only Cash and Card are accepted. For Card the kiosk sends the card type + last 4 digits
                    // (the full number / CVV never reach the server).
                    string requested = string.IsNullOrWhiteSpace(orderDto.PaymentMethod) ? "Cash" : orderDto.PaymentMethod.Trim();
                    string method;
                    string cardLast4 = "";
                    if (requested.Equals("Cash", StringComparison.OrdinalIgnoreCase))
                    {
                        method = "Cash";
                    }
                    else if (requested.Equals("Card", StringComparison.OrdinalIgnoreCase))
                    {
                        string cardType = (orderDto.CardType ?? "").Trim();
                        if (!DbHelper.CardTypes.TryGetValue(cardType, out string? shortName))
                            return BadRequest("Please choose the card type (Visa, Mastercard, BDO, BPI, ...).");
                        cardLast4 = (orderDto.CardLast4 ?? "").Trim();
                        if (cardLast4.Length != 4 || !cardLast4.All(char.IsAsciiDigit))
                            return BadRequest("Card details are incomplete.");
                        method = $"Card ({shortName})";
                    }
                    else
                    {
                        return BadRequest("Only Cash and Card payments are accepted.");
                    }
                    bool isCash = method == "Cash";

                    decimal tendered = orderDto.AmountTendered > 0 ? orderDto.AmountTendered : total;
                    if (isCash && tendered < total)
                        return BadRequest("The cash amount given is less than the total amount.");
                    decimal change = isCash ? tendered - total : 0m;

                    string cleanName = DbHelper.FitText(orderDto.CustomerName.Trim(), DbHelper.GetTextLimit(conn, "Customer", custCols.ContainsKey("CustomerName") ? "CustomerName" : "Name") ?? 100);
                    string customerType = string.IsNullOrWhiteSpace(orderDto.CustomerType) ? "Student" : orderDto.CustomerType.Trim();
                    int employeeId = orderDto.EmployeeID > 0 ? orderDto.EmployeeID : 1;
                    DateTime now = DbHelper.Now();

                    int? detailsLimit = DbHelper.GetTextLimit(conn, "Order", "ProductDetails");   // read before the transaction starts

                    tx = conn.BeginTransaction();

                    // 1. Customer (find by name or create)
                    string custIdCol = custCols.ContainsKey("CustomerID") ? "CustomerID" : "ID";
                    string custNameCol = custCols.ContainsKey("CustomerName") ? "CustomerName" : (custCols.ContainsKey("Name") ? "Name" : "CustomerName");
                    string phoneCol = custCols.ContainsKey("ContactNumber") ? "ContactNumber" : (custCols.ContainsKey("ContactNum") ? "ContactNum" : "Phone");

                    // Same University ID = same customer. Guests (no ID) are matched by name.
                    string uniCol = custCols.ContainsKey("UniversityIDNumber") ? "UniversityIDNumber" : "";
                    int customerId = 0;
                    object? existingId = null;

                    if (universityId.Length > 0 && uniCol.Length > 0)
                    {
                        existingId = DbHelper.Scalar(conn, tx,
                            $"SELECT [{custIdCol}] FROM [Customer] WHERE [{uniCol}] = ?", universityId);
                    }
                    else
                    {
                        existingId = uniCol.Length > 0
                            ? DbHelper.Scalar(conn, tx,
                                $"SELECT [{custIdCol}] FROM [Customer] WHERE [{custNameCol}] = ? AND ([{uniCol}] IS NULL OR [{uniCol}] = '' OR [{uniCol}] = 'N/A')",
                                cleanName)
                            : DbHelper.Scalar(conn, tx,
                                $"SELECT [{custIdCol}] FROM [Customer] WHERE [{custNameCol}] = ?", cleanName);
                    }

                    if (existingId != null && existingId != DBNull.Value)
                    {
                        customerId = Convert.ToInt32(existingId);
                    }
                    else
                    {
                        customerId = DbHelper.NextId(conn, tx, "Customer", custIdCol);
                        // A guest has no University ID. Store it empty; if the .accdb column refuses empty
                        // text (Allow Zero Length = No) fall back to "N/A" so the order still saves.
                        try
                        {
                            DbHelper.Insert(conn, tx, "Customer", custCols,
                                (custIdCol, customerId),
                                (custNameCol, cleanName),
                                ("CustomerType", customerType),
                                ("UniversityIDNumber", universityId),
                                (phoneCol, "N/A"));
                        }
                        catch (OleDbException) when (universityId.Length == 0)
                        {
                            DbHelper.Insert(conn, tx, "Customer", custCols,
                                (custIdCol, customerId),
                                (custNameCol, cleanName),
                                ("CustomerType", customerType),
                                ("UniversityIDNumber", "N/A"),
                                (phoneCol, "N/A"));
                        }
                    }

                    // 2. Order
                    string orderIdCol = orderCols.ContainsKey("OrderID") ? "OrderID" : "ID";
                    string dateCol = orderCols.ContainsKey("OrderDateTime") ? "OrderDateTime" : "OrderDate";
                    string totalCol = orderCols.ContainsKey("TotalAmount") ? "TotalAmount" : "Total";
                    string productSummary = string.Join(", ", lines.Select(l => $"{l.Name} (x{l.Quantity})"));
                    // big orders make a long summary: fit it to the real size of the column (the full list is in Order_Item)
                    productSummary = DbHelper.FitText(productSummary, detailsLimit);

                    int orderId = DbHelper.NextId(conn, tx, "Order", orderIdCol);
                    DbHelper.Insert(conn, tx, "Order", orderCols,
                        (orderIdCol, orderId),
                        ("CustomerID", customerId),
                        ("EmployeeID", employeeId),
                        ("ProductDetails", productSummary),
                        (dateCol, now),
                        ("Status", "Pending"),
                        (totalCol, total));

                    // 3. Order_Item
                    string itemIdCol = itemCols.ContainsKey("OrderItemID") ? "OrderItemID" : "ID";
                    string priceCol = itemCols.ContainsKey("UnitSellingPrice") ? "UnitSellingPrice" : "UnitPrice";
                    int nextItemId = DbHelper.NextId(conn, tx, "Order_Item", itemIdCol);
                    foreach (var line in lines)
                    {
                        DbHelper.Insert(conn, tx, "Order_Item", itemCols,
                            (itemIdCol, nextItemId),
                            ("OrderID", orderId),
                            ("ProductID", line.ProductID),
                            ("Quantity", line.Quantity),
                            (priceCol, line.UnitPrice));
                        nextItemId++;
                    }

                    // 4. Payment
                    int paymentId = DbHelper.NextId(conn, tx, "Payment", "PaymentID");
                    DbHelper.Insert(conn, tx, "Payment", payCols,
                        ("PaymentID", paymentId),
                        ("OrderID", orderId),
                        ("PaymentDateTime", now),
                        ("AmountPaid", total),
                        ("PaymentMethod", method));

                    // 5. Order_Payment (links the order to the payment)
                    int orderPaymentId = DbHelper.NextId(conn, tx, "Order_Payment", "OrderPaymentID");
                    DbHelper.Insert(conn, tx, "Order_Payment", orderPayCols,
                        ("OrderPaymentID", orderPaymentId),
                        ("OrderID", orderId),
                        ("PaymentID", paymentId),
                        ("PaymentDate", now),
                        ("AmountPaid", total));

                    // 6. Order_Receipt (created only after the payment rows above succeeded)
                    int receiptId = DbHelper.NextId(conn, tx, "Order_Receipt", "OrderReceiptID");
                    string receiptNumber = $"OR-{now:yyyyMMdd}-{orderId:D5}";
                    DbHelper.Insert(conn, tx, "Order_Receipt", receiptCols,
                        ("OrderReceiptID", receiptId),
                        ("OrderID", orderId),
                        ("ReceiptNumber", receiptNumber),
                        ("ReceiptDate", now),
                        ("TotalAmount", total));

                    tx.Commit();

                    return Ok(new
                    {
                        message = "Order placed and paid successfully!",
                        orderId,
                        customerName = cleanName,
                        customerType,
                        universityId,
                        paymentId,
                        orderPaymentId,
                        receiptId,
                        receiptNumber,
                        receiptDate = DbHelper.Fmt(now),
                        paymentMethod = method,
                        cardLast4,
                        subtotal,
                        tax,
                        totalAmount = total,
                        amountTendered = tendered,
                        change,
                        items = lines.Select(l => new
                        {
                            productName = l.Name,
                            quantity = l.Quantity,
                            unitPrice = l.UnitPrice,
                            lineTotal = l.UnitPrice * l.Quantity
                        }).ToList()
                    });
                }
                catch (Exception ex)
                {
                    DbHelper.SafeRollback(tx);
                    return StatusCode(500, $"Failed to process order: {ex.Message}");
                }
            }
        }
    }

    public class OrderCreateDto
    {
        public string CustomerName { get; set; } = "Guest";
        public string CustomerType { get; set; } = "Student";
        public string UniversityID { get; set; } = string.Empty;   // empty for guests
        public int EmployeeID { get; set; } = 1;
        public decimal TotalAmount { get; set; }
        public string PaymentMethod { get; set; } = "Cash";
        public decimal AmountTendered { get; set; }
        public string CardType { get; set; } = string.Empty;     // only when PaymentMethod = "Card"
        public string CardLast4 { get; set; } = string.Empty;    // only when PaymentMethod = "Card"
        public List<OrderItemDto> Items { get; set; } = new List<OrderItemDto>();
    }

    public class OrderItemDto
    {
        public int ProductID { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }
}
