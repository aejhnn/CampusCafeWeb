const API_URL = "http://localhost:5098/api";
const DEFAULT_PIN = "1234";

// shared data used by the Suppliers and Deliveries tabs
let allSuppliers = [];
let allIngredients = [];
let deliveryLines = [];
let allProducts = [];

function el(id) { return document.getElementById(id); }

// Payment method dropdowns (Supplier payment, Delivery payment, "+ Pay"):
// choosing Card shows the card form; this returns the text to save (e.g. "Card (Visa)")
// or null when the card details are not complete / not valid.
function onBizMethodChange(prefix) {
    const select = el(prefix === 'delivery-pay' ? 'delivery-pay-method' : `${prefix}-method`);
    CardForm.toggle(prefix, select.value === 'Card');
}

function readBizPaymentMethod(prefix) {
    const select = el(prefix === 'delivery-pay' ? 'delivery-pay-method' : `${prefix}-method`);
    if (select.value !== 'Card') return select.value;
    const card = CardForm.validate(prefix);
    if (!card.ok) { alert(card.error); return null; }
    return CardForm.methodLabel(prefix);
}

function esc(v) {
    return String(v ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

function peso(n) {
    return '₱' + Number(n || 0).toLocaleString('en-PH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

async function readError(res) {
    try { return await res.text(); } catch { return res.statusText; }
}

// Quantities in the cafe are whole numbers only (no decimals).
function parseWhole(raw, min = 1) {
    const t = String(raw ?? '').trim();
    if (!/^\d+$/.test(t)) return null;
    const n = parseInt(t, 10);
    return n >= min ? n : null;
}

// keeps "." "," "e" "+" "-" out of whole-number boxes while typing
function blockDecimalKeys(e) {
    if (['.', ',', 'e', 'E', '+', '-'].includes(e.key)) e.preventDefault();
}

let toastTimer = null;
function showToast(message, isError = false) {
    const t = el("toast");
    if (!t) return;
    t.textContent = message;
    t.className = "show" + (isError ? " error" : "");
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => { t.className = ""; }, 3200);
}

function loggedIn() { return !!sessionStorage.getItem("adminStaffName"); }

document.addEventListener("DOMContentLoaded", () => {
    checkSession();
});

function checkSession() {
    const activeStaff = sessionStorage.getItem("adminStaffName");
    if (activeStaff) {
        document.getElementById("auth-modal").style.display = "none";
        document.getElementById("logged-staff-badge").textContent = `Logged in as: ${activeStaff}`;
        loadAllModules();
    } else {
        document.getElementById("auth-modal").style.display = "flex";
    }
}

async function verifyPasscode() {
    const nameInput = document.getElementById("staff-name-input").value.trim();
    const pinInput = document.getElementById("staff-pin").value.trim();

    if (!nameInput) {
        showAuthError("Please enter your Staff Name.");
        return;
    }

    if (pinInput !== DEFAULT_PIN) {
        showAuthError("Incorrect Security PIN! Please try again.");
        return;
    }

    try {
        const res = await fetch(`${API_URL}/Employees/verify`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ name: nameInput, passcode: pinInput })
        });

        if (res.ok) {
            const data = await res.json();
            const cleanStaffName = data.name || nameInput;

            sessionStorage.setItem("adminStaffName", cleanStaffName);
            sessionStorage.setItem("adminStaffId", data.employeeId);

            document.getElementById("auth-modal").style.display = "none";
            document.getElementById("logged-staff-badge").textContent = `Logged in as: ${cleanStaffName}`;
            document.getElementById("auth-error-msg").style.display = "none";

            loadAllModules();
        } else {
            showAuthError("Access Denied: You are not a registered employee.");
        }
    } catch (err) {
        showAuthError("API connection error. Ensure backend server is running.");
    }
}

function showAuthError(msg) {
    const errorMsg = document.getElementById("auth-error-msg");
    const card = document.getElementById("auth-card");

    errorMsg.textContent = msg;
    errorMsg.style.display = "block";

    card.classList.remove("shake");
    void card.offsetWidth; // Force DOM reflow
    card.classList.add("shake");
}

function lockDashboard() {
    sessionStorage.removeItem("adminStaffName");
    sessionStorage.removeItem("adminStaffId");
    location.reload();
}

function loadAllModules() {
    loadProducts();
    loadIngredients();
    loadEmployees();
    loadSuppliers();
    loadSupplyHistory();
    loadDeliveries();
}

function switchTab(tabName, evt) {
    document.querySelectorAll('.tab-btn').forEach(btn => btn.classList.remove('active'));
    document.querySelectorAll('.module-section').forEach(mod => mod.classList.remove('active'));

    if (evt) evt.target.classList.add('active');
    document.getElementById(`mod-${tabName}`).classList.add('active');

    refreshTab(tabName);   // always show what is really in the .accdb right now
}

function refreshTab(tabName) {
    if (tabName === 'ingredients') loadIngredients();
    else if (tabName === 'employees') loadEmployees();
    else if (tabName === 'deliveries') { loadDeliveries(); loadIngredients(); }
}

// Coming back to this browser tab (e.g. after editing in Access): pull the latest stock and deliveries.
window.addEventListener('focus', () => {
    if (!loggedIn()) return;
    loadIngredients();
    loadDeliveries();
});

// ---------------------------------------------------------
// 1. PRODUCTS MODULE
// ---------------------------------------------------------
async function loadProducts() {
    try {
        const res = await fetch(`${API_URL}/Products`);
        if (!res.ok) throw new Error("API error");
        const products = await res.json();
        renderProducts(products);
    } catch (err) {
        console.error("Error loading products:", err);
    }
}

function isAvailable(status) {
    if (status === null || status === undefined) return true;
    const s = String(status).trim().toLowerCase();
    return s !== 'unavailable' && s !== 'false' && s !== '0';
}

function renderProducts(products) {
    const coffeeContainer = document.getElementById("coffee-products");
    const nonCoffeeContainer = document.getElementById("non-coffee-products");
    const teaContainer = document.getElementById("tea-products");
    const chocolateContainer = document.getElementById("chocolate-products");

    if (coffeeContainer) coffeeContainer.innerHTML = "";
    if (nonCoffeeContainer) nonCoffeeContainer.innerHTML = "";
    if (teaContainer) teaContainer.innerHTML = "";
    if (chocolateContainer) chocolateContainer.innerHTML = "";

    // products the manager removed are hidden completely
    products = products.filter(p => String(p.availabilityStatus || '').trim().toLowerCase() !== 'removed');
    allProducts = products;
    products.forEach(p => {
        const category = (p.category || '').toLowerCase();
        const available = isAvailable(p.availabilityStatus);

        const itemHtml = `
            <div class="product-item">
                <div>
                    <strong>${esc(p.productName)}</strong>
                    <div style="font-size:12px; color:#64748b;">₱${p.price.toFixed(2)} - ${esc(p.description || 'No description')}</div>
                </div>
                <div class="stock-ctrl">
                    <button type="button" class="avail-toggle ${available ? 'is-on' : 'is-off'}"
                            title="${available ? 'Click to mark as Unavailable' : 'Click to mark as Available'}"
                            onclick="toggleAvailability(${Number(p.productID)}, this)">
                        ${available ? 'Available' : 'Unavailable'}
                    </button>
                    <button type="button" class="btn-remove" title="Remove this product from the menu"
                            onclick="removeProduct(${Number(p.productID)})">Remove</button>
                </div>
            </div>
        `;

        if (category.includes('coffee') && !category.includes('non')) {
            if (coffeeContainer) coffeeContainer.innerHTML += itemHtml;
        } else if (category.includes('non')) {
            if (nonCoffeeContainer) nonCoffeeContainer.innerHTML += itemHtml;
        } else if (category.includes('tea') || category.includes('milk')) {
            if (teaContainer) teaContainer.innerHTML += itemHtml;
        } else if (category.includes('chocolate')) {
            if (chocolateContainer) chocolateContainer.innerHTML += itemHtml;
        } else {
            if (coffeeContainer) coffeeContainer.innerHTML += itemHtml;
        }
    });
}

async function addProduct(evt) {
    if (evt) evt.preventDefault();

    const name = document.getElementById("product-name").value;
    const category = document.getElementById("product-category").value;
    const price = parseFloat(document.getElementById("product-price").value);
    const description = document.getElementById("product-desc").value;
    const isAvailableVal = document.getElementById("product-available").checked ? "Available" : "Unavailable";

    const payload = {
        productName: name,
        category: category,
        price: price,
        description: description,
        availabilityStatus: isAvailableVal
    };

    try {
        const res = await fetch(`${API_URL}/Products`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload)
        });

        if (res.ok) {
            alert("🎉 Product added successfully!");
            document.getElementById("add-product-form").reset();
            loadProducts();
        } else {
            alert("Failed to add product.");
        }
    } catch (err) {
        console.error("Error adding product:", err);
    }
}

// Click the Available / Unavailable badge to switch it (like a toggle button)
async function toggleAvailability(id, btn) {
    const prod = allProducts.find(p => Number(p.productID) === id);
    if (!prod) return;
    const newStatus = isAvailable(prod.availabilityStatus) ? "Unavailable" : "Available";
    if (btn) btn.disabled = true;

    try {
        const res = await fetch(`${API_URL}/Products/${id}/toggle-status`, {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ availabilityStatus: newStatus })
        });
        if (!res.ok) { alert(await readError(res)); if (btn) btn.disabled = false; return; }
        showToast(`${prod.productName} is now ${newStatus}.`);
        await loadProducts();
    } catch (err) {
        console.error("Error changing availability:", err);
        alert("Network error: make sure the C# API is running.");
        if (btn) btn.disabled = false;
    }
}

// "Remove" button next to each product in the Menu Products tab.
// The product disappears from the menu completely (Management Board AND Customer Kiosk).
async function removeProduct(id) {
    const prod = allProducts.find(p => Number(p.productID) === id);
    const label = prod ? prod.productName : `#${id}`;
    if (!confirm(`Remove "${label}" from the menu?`)) return;

    try {
        const res = await fetch(`${API_URL}/Products/${id}`, { method: "DELETE" });
        if (!res.ok) { alert(await readError(res)); return; }
        showToast(`🗑️ ${label} removed from the menu.`);
        await loadProducts();
    } catch (err) {
        console.error("Error removing product:", err);
        alert("Network error: make sure the C# API is running.");
    }
}

// ---------------------------------------------------------
// 2. INGREDIENTS MODULE
// ---------------------------------------------------------
async function loadIngredients() {
    try {
        const res = await fetch(`${API_URL}/Ingredients`);
        if (!res.ok) return;
        const ingredients = await res.json();
        allIngredients = ingredients;
        renderIngredients(ingredients);
        fillSupplyIngredientSelect();
    } catch (err) {
        console.error("Error loading ingredients:", err);
    }
}

// 0 = Unavailable, 1-9 (below 10) = Low stock, 10 and above = Available
function stockStatus(qty) {
    if (qty <= 0) return { label: 'Unavailable', css: 'status-unavailable' };
    if (qty < 10) return { label: 'Low stock', css: 'status-low' };
    return { label: 'Available', css: 'status-available' };
}

function renderIngredients(ingredients) {
    const tableBody = document.getElementById("ingredients-table-body");
    if (!tableBody) return;

    if (!ingredients || ingredients.length === 0) {
        tableBody.innerHTML = `<tr><td colspan="6" style="color:#94a3b8;">No ingredients records found.</td></tr>`;
        return;
    }

    tableBody.innerHTML = ingredients.map(ing => {
        const id = Number(ing.ingredientID ?? ing.ingredientId ?? ing.id);
        const qty = Math.round(Number(ing.stockQuantity ?? ing.quantityInStock ?? ing.stock ?? 0));
        const status = stockStatus(qty);
        return `
        <tr>
            <td>${esc(id)}</td>
            <td>${esc(ing.ingredientName || ing.name || '-')}</td>
            <td><strong>${qty}</strong></td>
            <td>${esc(ing.unit || ing.unitOfMeasure || 'units')}</td>
            <td>
                <span class="${status.css}">${status.label}</span>
            </td>
            <td>
                <div class="stock-ctrl">
                    <button type="button" class="btn-circle btn-minus" title="Subtract this amount from stock"
                            onclick="adjustStock(${id}, -1)" ${qty <= 0 ? 'disabled' : ''}>−</button>
                    <input type="number" id="adj-${id}" class="amt-input" value="1" min="1" step="1" inputmode="numeric"
                           title="Amount to add or subtract" onkeydown="blockDecimalKeys(event)">
                    <button type="button" class="btn-circle btn-plus" title="Add this amount to stock"
                            onclick="adjustStock(${id}, 1)">+</button>
                    <button type="button" class="btn-remove" title="Remove this ingredient from the list"
                            onclick="removeIngredient(${id})">Remove</button>
                </div>
            </td>
        </tr>`;
    }).join('');
}

// "+" form: add a brand-new ingredient
async function addIngredient(evt) {
    if (evt) evt.preventDefault();

    const name = el("ingredient-name").value.trim();
    const unit = el("ingredient-unit").value.trim() || "pcs";
    const qty = parseWhole(el("ingredient-qty").value, 0);

    if (!name) { alert("Please enter the ingredient name."); return; }
    if (qty === null) { alert("Starting quantity must be a whole number (0 or more, no decimals)."); return; }

    try {
        const res = await fetch(`${API_URL}/Ingredients`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ ingredientName: name, unit, stockQuantity: qty })
        });
        if (!res.ok) { alert("Could not add the ingredient:\n" + await readError(res)); return; }

        el("ingredient-form").reset();
        el("ingredient-unit").value = "pcs";
        el("ingredient-qty").value = "0";
        showToast(`✅ ${name} added to the ingredient list.`);
        await loadIngredients();
    } catch (err) {
        console.error("Error adding ingredient:", err);
        alert("Network error: make sure the C# API is running.");
    }
}

// "+" / "-" buttons in the table: sign is +1 or -1, the amount comes from the little box
async function adjustStock(id, sign) {
    const box = el(`adj-${id}`);
    const amount = parseWhole(box ? box.value : "1", 1);
    if (amount === null) {
        alert("Enter a whole number of 1 or more (no decimals).");
        if (box) box.focus();
        return;
    }

    try {
        const res = await fetch(`${API_URL}/Ingredients/${id}/adjust`, {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ delta: sign * amount })
        });
        if (!res.ok) { alert(await readError(res)); return; }

        const data = await res.json();
        const ing = allIngredients.find(i => Number(i.ingredientID) === id);
        showToast(`${sign > 0 ? '＋' : '−'}${amount} ${ing ? ing.ingredientName : 'ingredient'} → now ${data.newQuantity} ${ing ? ing.unit : ''}`.trim());
        await loadIngredients();   // re-read from the database so the table is always the real stock
    } catch (err) {
        console.error("Error adjusting stock:", err);
        alert("Network error: make sure the C# API is running.");
    }
}

async function removeIngredient(id) {
    const ing = allIngredients.find(i => Number(i.ingredientID) === id);
    const label = ing ? ing.ingredientName : `#${id}`;
    if (!confirm(`Remove "${label}" from the ingredient list?`)) return;

    try {
        const res = await fetch(`${API_URL}/Ingredients/${id}`, { method: "DELETE" });
        if (!res.ok) { alert(await readError(res)); return; }
        showToast(`${label} removed.`);
        await loadIngredients();
        loadSuppliers();
    } catch (err) {
        console.error("Error removing ingredient:", err);
        alert("Network error: make sure the C# API is running.");
    }
}

// ---------------------------------------------------------
// 3. EMPLOYEES MODULE
// ---------------------------------------------------------
let allEmployees = [];

async function loadEmployees() {
    try {
        const res = await fetch(`${API_URL}/Employees`);
        if (!res.ok) return;
        allEmployees = await res.json();
        renderEmployees(allEmployees);
    } catch (err) {
        console.error("Error loading employees:", err);
    }
}

function isKioskAccount(emp) {
    return /kiosk/i.test(emp.name || '');
}

function renderEmployees(employees) {
    const tableBody = document.getElementById("employees-table-body");
    if (!tableBody) return;

    const summary = el("employee-summary");
    if (summary) {
        const full = (employees || []).filter(e => e.workSchedule === 'Full-Time').length;
        const part = (employees || []).filter(e => e.workSchedule === 'Part-Time').length;
        summary.textContent = employees && employees.length
            ? `${employees.length} employees · ${full} Full-Time · ${part} Part-Time`
            : '';
    }

    if (!employees || employees.length === 0) {
        tableBody.innerHTML = `<tr><td colspan="6" style="color:#94a3b8;">No employee records found.</td></tr>`;
        return;
    }

    tableBody.innerHTML = employees.map(emp => {
        const id = Number(emp.employeeID);
        const sched = emp.workSchedule || '';
        const known = sched === 'Full-Time' || sched === 'Part-Time';
        const kiosk = isKioskAccount(emp);

        return `
            <tr>
                <td>${esc(id)}</td>
                <td>${esc(emp.name || '-')}</td>
                <td style="min-width:150px;">
                    <input type="text" id="emp-pos-${id}" class="cell-input" list="role-options" maxlength="50"
                           value="${esc(emp.position)}" placeholder="Set a role..."
                           onkeydown="if(event.key==='Enter'){saveEmployee(${id});}">
                </td>
                <td style="min-width:120px;">
                    <select id="emp-sched-${id}" class="cell-select">
                        ${known ? '' : '<option value="" selected>Not set</option>'}
                        <option value="Full-Time" ${sched === 'Full-Time' ? 'selected' : ''}>Full-Time</option>
                        <option value="Part-Time" ${sched === 'Part-Time' ? 'selected' : ''}>Part-Time</option>
                    </select>
                </td>
                <td>${esc(emp.contactNumber || 'N/A')}</td>
                <td>
                    <div class="stock-ctrl">
                        <button type="button" class="btn-save" onclick="saveEmployee(${id})">Save</button>
                        <button type="button" class="btn-remove" onclick="removeEmployee(${id})"
                                ${kiosk ? 'disabled title="Kiosk system accounts cannot be removed"' : ''}>− Remove</button>
                    </div>
                </td>
            </tr>
        `;
    }).join('');
}

// "+" form: add an employee
async function addEmployee(evt) {
    if (evt) evt.preventDefault();

    const name = el("employee-name").value.trim();
    const contactNumber = el("employee-contact").value.trim();
    const position = el("employee-position").value.trim();
    const workSchedule = el("employee-schedule").value;

    if (!name) { alert("Please enter the employee's full name."); return; }
    if (!position) { alert("Please enter a role / position."); return; }

    try {
        const res = await fetch(`${API_URL}/Employees`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ name, contactNumber, position, workSchedule })
        });
        if (!res.ok) { alert("Could not add the employee:\n" + await readError(res)); return; }

        el("employee-form").reset();
        showToast(`✅ ${name} added as ${position} (${workSchedule}).`);
        await loadEmployees();
    } catch (err) {
        console.error("Error adding employee:", err);
        alert("Network error: make sure the C# API is running.");
    }
}

// Save the role / position and schedule shown in the row
async function saveEmployee(id) {
    const position = el(`emp-pos-${id}`).value.trim();
    const workSchedule = el(`emp-sched-${id}`).value;

    if (!position) { alert("Role / position cannot be empty."); return; }
    if (!workSchedule) { alert("Please choose Full-Time or Part-Time."); return; }

    try {
        const res = await fetch(`${API_URL}/Employees/${id}`, {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ position, workSchedule })
        });
        if (!res.ok) { alert("Could not save the changes:\n" + await readError(res)); return; }

        const emp = allEmployees.find(e => Number(e.employeeID) === id);
        showToast(`💾 ${emp ? emp.name : 'Employee'} is now ${position} (${workSchedule}).`);
        await loadEmployees();
    } catch (err) {
        console.error("Error saving employee:", err);
        alert("Network error: make sure the C# API is running.");
    }
}

// "-" button: remove an employee
async function removeEmployee(id) {
    const emp = allEmployees.find(e => Number(e.employeeID) === id);
    const label = emp ? emp.name : `#${id}`;

    if (String(id) === sessionStorage.getItem("adminStaffId")) {
        showToast("You can't remove the account you are logged in with.", true);
        return;
    }
    if (!confirm(`Remove ${label} from the employee list?`)) return;

    try {
        const res = await fetch(`${API_URL}/Employees/${id}`, { method: "DELETE" });
        if (!res.ok) { alert(await readError(res)); return; }
        showToast(`${label} removed.`);
        await loadEmployees();
    } catch (err) {
        console.error("Error removing employee:", err);
        alert("Network error: make sure the C# API is running.");
    }
}

// ---------------------------------------------------------
// 4. SUPPLIERS MODULE  (Supplier_Ingredient + Supplier_Payment + Supplier_Receipt)
// ---------------------------------------------------------
async function loadSuppliers() {
    try {
        const res = await fetch(`${API_URL}/Suppliers`);
        if (!res.ok) return;
        const suppliers = await res.json();
        allSuppliers = suppliers;
        renderSuppliers(suppliers);
        fillSupplierSelects();
    } catch (err) {
        console.error("Error loading suppliers:", err);
    }
}

function renderSuppliers(suppliers) {
    const tableBody = el("suppliers-table-body");
    if (!tableBody) return;

    if (!suppliers || suppliers.length === 0) {
        tableBody.innerHTML = `<tr><td colspan="4" style="color:#94a3b8;">No supplier records found.</td></tr>`;
        return;
    }

    tableBody.innerHTML = suppliers.map(sup => `
        <tr>
            <td>${esc(sup.supplierID ?? '-')}</td>
            <td>${esc(sup.supplierName || '-')}</td>
            <td>${esc(sup.contactPerson || 'N/A')}</td>
            <td>${esc(sup.phone || 'N/A')}</td>
        </tr>
    `).join('');
}

function fillSupplierSelects() {
    const options = '<option value="">-- Choose supplier --</option>' +
        allSuppliers.map(s => `<option value="${s.supplierID}">${esc(s.supplierName)}</option>`).join('');

    const supplySel = el("supply-supplier");
    const deliverySel = el("delivery-supplier");
    if (supplySel) supplySel.innerHTML = options;
    if (deliverySel) deliverySel.innerHTML = options;
}

// Fills the suggestion list of the Supplier tab's ingredient box (the box also accepts any typed name)
function fillSupplyIngredientSelect() {
    const list = el("supply-ingredient-options");
    if (!list) return;
    list.innerHTML = allIngredients.map(i =>
        `<option value="${esc(i.ingredientName)}">${esc(i.unit)}</option>`).join('');
    onIngredientTyped('supply');
}

// Turns what was typed in an ingredient box into { id } (existing ingredient) or { name } (new one)
function findIngredientByName(name) {
    const t = String(name || '').trim().toLowerCase();
    if (!t) return null;
    return allIngredients.find(i => String(i.ingredientName).trim().toLowerCase() === t) || null;
}

function resolveTypedIngredient(prefix) {
    const typed = el(`${prefix}-ingredient`).value.trim();
    if (!typed) return null;
    const found = findIngredientByName(typed);
    if (found) return { id: Number(found.ingredientID), name: found.ingredientName, unit: found.unit, isNew: false };
    const unit = (el(`${prefix}-unit`).value || '').trim() || 'pcs';
    return { id: 0, name: typed, unit, isNew: true };
}

// Shows the "Unit" box only when the typed name is not an existing ingredient
function onIngredientTyped(prefix) {
    const input = el(`${prefix}-ingredient`);
    const row = el(`${prefix}-unit-row`);
    if (!input || !row) return;
    const typed = input.value.trim();
    row.style.display = (typed && !findIngredientByName(typed)) ? 'block' : 'none';
}

async function submitSupply(evt) {
    if (evt) evt.preventDefault();

    const supplierID = parseInt(el("supply-supplier").value);
    const ingredient = resolveTypedIngredient('supply');
    const amountPaid = parseFloat(el("supply-amount").value);

    if (!supplierID) { alert("Please choose a supplier."); return; }
    if (!ingredient) { alert("Please choose or type an ingredient."); return; }
    if (!(amountPaid > 0)) { alert("Please enter a payment amount greater than 0."); return; }
    const paymentMethod = readBizPaymentMethod('supply');
    if (!paymentMethod) return;

    try {
        const res = await fetch(`${API_URL}/Suppliers/supply`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                supplierID,
                ingredientID: ingredient.id,
                ingredientName: ingredient.name,
                unit: ingredient.unit,
                amountPaid,
                paymentMethod
            })
        });

        if (!res.ok) {
            alert("Could not save the supplier payment:\n" + await readError(res));
            return;
        }

        const data = await res.json();
        el("supply-form").reset();
        onBizMethodChange('supply');
        onIngredientTyped('supply');
        showSupplyReceipt(data);
        loadSupplyHistory();
        if (data.ingredientCreated) { showToast(`✅ New ingredient "${data.ingredientName}" added.`); }
        loadIngredients();   // a typed ingredient is now in the list
    } catch (err) {
        console.error("Error saving supply payment:", err);
        alert("Network error: make sure the C# API is running.");
    }
}

let loadedSupplyReceipts = [];

function viewSupplyReceipt(idx) {
    const r = loadedSupplyReceipts[idx];
    if (!r) return;
    showSupplyReceipt({
        receiptNumber: r.receiptNumber,
        receiptDate: r.receiptDate,
        supplierName: r.supplierName,
        amountPaid: r.totalAmount
    });
}

async function loadSupplyHistory() {
    const body = el("supply-history-body");
    if (!body) return;
    try {
        const res = await fetch(`${API_URL}/Suppliers/supply-history`);
        if (!res.ok) return;
        const list = await res.json();

        if (!list.length) {
            body.innerHTML = `<tr><td colspan="5" style="color:#94a3b8;">No supplier receipts yet.</td></tr>`;
            return;
        }

        loadedSupplyReceipts = list;
        body.innerHTML = list.map((r, idx) => `
            <tr>
                <td>${esc(r.receiptNumber)}</td>
                <td>${esc(r.supplierName)}</td>
                <td>${esc(r.receiptDate)}</td>
                <td>${peso(r.totalAmount)}</td>
                <td><button class="btn-small" onclick="viewSupplyReceipt(${idx})">View</button></td>
            </tr>
        `).join('');
    } catch (err) {
        console.error("Error loading supplier receipts:", err);
    }
}

function showSupplyReceipt(d) {
    const ingredientLine = d.ingredientName
        ? `<div class="receipt-line"><span>Ingredient</span><span>${esc(d.ingredientName)}${d.unit ? ' (' + esc(d.unit) + ')' : ''}</span></div>`
        : '';
    const methodLine = d.paymentMethod
        ? `<div class="receipt-line"><span>Payment Method</span><span>${esc(d.paymentMethod)}</span></div>`
        : '';

    el("receipt-content").innerHTML = `
        <div class="receipt-title">☕ Campus Café</div>
        <div class="receipt-sub">SUPPLIER PAYMENT RECEIPT</div>
        <div class="receipt-line"><span>Receipt No.</span><strong>${esc(d.receiptNumber)}</strong></div>
        <div class="receipt-line"><span>Date</span><span>${esc(d.receiptDate)}</span></div>
        <div class="receipt-line"><span>Supplier</span><span>${esc(d.supplierName)}</span></div>
        ${ingredientLine}
        ${methodLine}
        <hr class="receipt-divider">
        <div class="receipt-line receipt-total"><span>Total Paid</span><span>${peso(d.amountPaid)}</span></div>
    `;
    el("receipt-modal").style.display = "flex";
}

// ---------------------------------------------------------
// 5. DELIVERIES MODULE  (Delivery_Receipt + Delivery_Item + Delivery_Payment)
// ---------------------------------------------------------
async function onDeliverySupplierChange() {
    const supplierId = el("delivery-supplier").value;
    const list = el("delivery-ingredient-options");
    const hint = el("delivery-ingredient-hint");

    deliveryLines = [];
    renderDeliveryLines();
    el("delivery-ingredient").value = "";
    onIngredientTyped('delivery');

    if (!supplierId) {
        list.innerHTML = "";
        hint.textContent = "";
        return;
    }

    let items = [];
    try {
        const res = await fetch(`${API_URL}/Suppliers/${supplierId}/ingredients`);
        if (res.ok) items = await res.json();
    } catch (err) {
        console.error("Error loading supplier ingredients:", err);
    }

    if (items.length > 0) {
        hint.textContent = "Suggestions show the ingredients this supplier provides. You can also type any other ingredient name.";
    } else {
        items = allIngredients;
        hint.textContent = "This supplier has no linked ingredients yet, so all ingredients are suggested. You can also type a new one.";
    }

    list.innerHTML = items.map(i =>
        `<option value="${esc(i.ingredientName)}">${esc(i.unit || '')}</option>`
    ).join('');
}

function addDeliveryLine() {
    const ingredient = resolveTypedIngredient('delivery');
    const qty = parseWhole(el("delivery-qty").value, 1);

    if (!el("delivery-supplier").value) { alert("Please choose a supplier first."); return; }
    if (!ingredient) { alert("Please choose or type an ingredient."); return; }
    if (qty === null) { alert("Quantity must be a whole number of 1 or more (no decimals)."); return; }

    // same ingredient twice -> add up the quantity (an existing one by ID, a typed new one by name)
    const existing = deliveryLines.find(l => ingredient.isNew
        ? (l.ingredientID === 0 && l.name.toLowerCase() === ingredient.name.toLowerCase())
        : l.ingredientID === ingredient.id);

    if (existing) {
        existing.quantity += qty;
    } else {
        deliveryLines.push({
            ingredientID: ingredient.id,
            name: ingredient.name,
            unit: ingredient.unit || "",
            quantity: qty,
            isNew: ingredient.isNew
        });
    }

    el("delivery-qty").value = "";
    el("delivery-ingredient").value = "";
    onIngredientTyped('delivery');
    renderDeliveryLines();
}

function removeDeliveryLine(index) {
    deliveryLines.splice(index, 1);
    renderDeliveryLines();
}

function renderDeliveryLines() {
    const body = el("delivery-lines-body");
    if (!body) return;

    if (deliveryLines.length === 0) {
        body.innerHTML = `<tr><td colspan="3" style="color:#94a3b8;">No items added yet.</td></tr>`;
        return;
    }

    body.innerHTML = deliveryLines.map((l, i) => `
        <tr>
            <td>${esc(l.name)}${l.isNew ? ' <span class="badge badge-part">new</span>' : ''}</td>
            <td>${l.quantity} ${esc(l.unit)}</td>
            <td><button type="button" class="btn-small btn-danger" onclick="removeDeliveryLine(${i})">Remove</button></td>
        </tr>
    `).join('');
}

async function submitDelivery(evt) {
    if (evt) evt.preventDefault();

    const supplierID = parseInt(el("delivery-supplier").value);
    const amountPaid = parseFloat(el("delivery-amount").value);

    if (!supplierID) { alert("Please choose a supplier."); return; }
    if (deliveryLines.length === 0) { alert("Add at least one delivery item."); return; }
    if (!(amountPaid > 0)) { alert("Please enter a payment amount greater than 0."); return; }
    if (deliveryLines.some(l => !Number.isInteger(l.quantity) || l.quantity < 1)) {
        alert("Every delivery quantity must be a whole number of 1 or more.");
        return;
    }
    const paymentMethod = readBizPaymentMethod('delivery');
    if (!paymentMethod) return;

    const payload = {
        supplierID,
        items: deliveryLines.map(l => ({
            ingredientID: l.ingredientID,
            ingredientName: l.name,
            unit: l.unit,
            quantity: Math.trunc(l.quantity)
        })),
        amountPaid,
        paymentMethod
    };

    try {
        const res = await fetch(`${API_URL}/Deliveries`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload)
        });

        if (!res.ok) {
            alert("Could not save the delivery:\n" + await readError(res));
            return;
        }

        const delivery = await res.json();

        el("delivery-form").reset();
        onBizMethodChange('delivery');
        deliveryLines = [];
        renderDeliveryLines();
        el("delivery-ingredient-options").innerHTML = "";
        el("delivery-ingredient-hint").textContent = "";
        onIngredientTyped('delivery');

        await loadIngredients();   // stock went up in the .accdb - re-read it so the table matches
        showDeliveryReceipt(delivery, true);
        loadDeliveries();
    } catch (err) {
        console.error("Error saving delivery:", err);
        alert("Network error: make sure the C# API is running.");
    }
}

let loadedDeliveries = [];

async function loadDeliveries() {
    const body = el("deliveries-table-body");
    if (!body) return;
    try {
        const res = await fetch(`${API_URL}/Deliveries`);
        if (!res.ok) return;
        loadedDeliveries = await res.json();

        if (!loadedDeliveries.length) {
            body.innerHTML = `<tr><td colspan="7" style="color:#94a3b8;">No deliveries recorded yet.</td></tr>`;
            return;
        }

        body.innerHTML = loadedDeliveries.map(d => `
            <tr>
                <td>${esc(d.receiptNumber)}</td>
                <td>${esc(d.supplierName)}</td>
                <td>${esc(d.deliveryDate)}</td>
                <td>${(d.items || []).length}</td>
                <td>${esc(paymentMethodsOf(d))}</td>
                <td>${peso(d.totalPaid)}</td>
                <td>
                    <div class="stock-ctrl">
                        <button class="btn-small" onclick="viewDelivery(${d.deliveryID})">View</button>
                        <button class="btn-small" style="background:#dcfce7; color:#166534;" onclick="openDeliveryPayment(${d.deliveryID})">+ Pay</button>
                    </div>
                </td>
            </tr>
        `).join('');
    } catch (err) {
        console.error("Error loading deliveries:", err);
    }
}

// e.g. "Cash" or "Cash, Card (Visa)" when a delivery was paid in several payments
function paymentMethodsOf(d) {
    const methods = [...new Set((d.payments || []).map(p => p.paymentMethod).filter(Boolean))];
    return methods.length ? methods.join(', ') : '-';
}

let payingDeliveryId = null;

function openDeliveryPayment(id) {
    const d = loadedDeliveries.find(x => x.deliveryID === id);
    if (!d) return;
    payingDeliveryId = id;
    el("delivery-pay-title").textContent = `${d.receiptNumber} · ${d.supplierName} · paid so far ${peso(d.totalPaid)}`;
    el("delivery-pay-amount").value = "";
    el("delivery-pay-method").value = "Cash";
    CardForm.mount("delivery-pay");
    CardForm.reset("delivery-pay");
    onBizMethodChange("delivery-pay");
    el("delivery-pay-modal").style.display = "flex";
    el("delivery-pay-amount").focus();
}

function closeDeliveryPayment(evt) {
    if (!evt || evt.target.id === "delivery-pay-modal") {
        el("delivery-pay-modal").style.display = "none";
        payingDeliveryId = null;
    }
}

async function submitDeliveryPayment() {
    const amountPaid = parseFloat(el("delivery-pay-amount").value);
    if (!payingDeliveryId) return;
    if (!(amountPaid > 0)) { alert("Please enter a payment amount greater than 0."); return; }
    const paymentMethod = readBizPaymentMethod('delivery-pay');
    if (!paymentMethod) return;

    try {
        const res = await fetch(`${API_URL}/Deliveries/${payingDeliveryId}/payments`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ amountPaid, paymentMethod })
        });
        if (!res.ok) { alert("Could not save the payment:\n" + await readError(res)); return; }

        const delivery = await res.json();
        closeDeliveryPayment();
        await loadDeliveries();
        showToast(`💳 Payment of ${peso(amountPaid)} saved for ${delivery.receiptNumber}.`);
        showDeliveryReceipt(delivery);
    } catch (err) {
        console.error("Error saving delivery payment:", err);
        alert("Network error: make sure the C# API is running.");
    }
}

function viewDelivery(id) {
    const d = loadedDeliveries.find(x => x.deliveryID === id);
    if (d) showDeliveryReceipt(d);
}

function showDeliveryReceipt(d, justSaved = false) {
    const itemLines = (d.items || []).map(i => `
        <div class="receipt-line"><span>${esc(i.ingredientName)}</span><span>${Math.round(Number(i.quantityReceived))} ${esc(i.unit)}</span></div>
    `).join('');

    // right after saving: show the stock as it is now stored in the database
    const syncNote = justSaved ? `
        <div class="sync-note no-print">✅ Saved to the database. Stock updated:<br>
            ${(d.items || []).map(i => `${esc(i.ingredientName)} → <b>${i.currentStock} ${esc(i.unit)}</b>`).join('<br>')}
        </div>` : '';

    const payLines = (d.payments || []).map(p => `
        <div class="receipt-line"><span>${esc(p.paymentMethod)} · ${esc(p.paymentDate)}</span><span>${peso(p.amountPaid)}</span></div>
    `).join('');

    el("receipt-content").innerHTML = `
        <div class="receipt-title">☕ Campus Café</div>
        <div class="receipt-sub">DELIVERY RECEIPT</div>
        <div class="receipt-line"><span>Receipt No.</span><strong>${esc(d.receiptNumber)}</strong></div>
        <div class="receipt-line"><span>Date</span><span>${esc(d.deliveryDate)}</span></div>
        <div class="receipt-line"><span>Supplier</span><span>${esc(d.supplierName)}</span></div>
        <hr class="receipt-divider">
        <div style="font-size:12px; font-weight:800; color:#475569;">ITEMS DELIVERED</div>
        ${itemLines}
        <hr class="receipt-divider">
        <div style="font-size:12px; font-weight:800; color:#475569;">PAYMENT</div>
        ${payLines}
        <div class="receipt-line receipt-total"><span>Total Paid</span><span>${peso(d.totalPaid)}</span></div>
        ${syncNote}
    `;
    el("receipt-modal").style.display = "flex";
}

function closeReceipt(evt) {
    if (!evt || evt.target.id === "receipt-modal") {
        el("receipt-modal").style.display = "none";
    }
}


// ---------------------------------------------------------
// 6. INVENTORY SEARCH  (added feature - filters the Ingredients table)
//    Does not change the existing loading/rendering code: it wraps
//    renderIngredients() so the search stays applied whenever the
//    table refreshes (e.g. after a delivery raises the stock).
// ---------------------------------------------------------
const renderIngredientsOriginal = renderIngredients;

// One searchable text per ingredient: ID, name, quantity, unit and status
function ingredientSearchText(ing) {
    const qty = Math.round(Number(ing.stockQuantity ?? ing.quantityInStock ?? ing.stock ?? 0));
    const statusLabel = stockStatus(qty).label.toLowerCase();   // same rule the table uses
    return [
        ing.ingredientID ?? ing.ingredientId ?? ing.id ?? '',
        ing.ingredientName || ing.name || '',
        qty,
        ing.unit || ing.unitOfMeasure || 'units',
        statusLabel
    ].join(' ').toLowerCase();
}

// Every word typed must match somewhere (so "milk kg" or "fresh milk" both work)
function filterIngredients(list, term) {
    const words = term.toLowerCase().split(/\s+/).filter(Boolean);
    if (words.length === 0) return list;
    return list.filter(ing => {
        const text = ingredientSearchText(ing);
        return words.every(w => text.includes(w));
    });
}

function updateIngredientSearchCount(shown, total, searching) {
    const counter = document.getElementById("ingredient-search-count");
    if (!counter) return;
    counter.textContent = searching ? `Showing ${shown} of ${total}` : (total ? `${total} ingredients` : "");
}

renderIngredients = function (ingredients) {
    const input = document.getElementById("ingredient-search");
    const term = input ? input.value.trim() : "";
    const list = Array.isArray(ingredients) ? ingredients : [];

    if (!term) {
        renderIngredientsOriginal(ingredients);
        updateIngredientSearchCount(list.length, list.length, false);
        return;
    }

    const filtered = filterIngredients(list, term);
    const tableBody = document.getElementById("ingredients-table-body");

    if (filtered.length === 0 && tableBody) {
        tableBody.innerHTML = `<tr><td colspan="6" style="color:#94a3b8;">No ingredients match "${esc(term)}".</td></tr>`;
    } else {
        renderIngredientsOriginal(filtered);
    }
    updateIngredientSearchCount(filtered.length, list.length, true);
};

function searchIngredients() {
    renderIngredients(allIngredients);
}

function clearIngredientSearch() {
    const input = document.getElementById("ingredient-search");
    if (input) {
        input.value = "";
        input.focus();
    }
    renderIngredients(allIngredients);
}
