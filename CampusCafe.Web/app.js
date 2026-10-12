const API_URL = "http://localhost:5005/api";

document.addEventListener("DOMContentLoaded", () => {
    loadAllModules();
});

function loadAllModules() {
    loadProducts();
    loadIngredients();
    loadEmployees();
    loadSuppliers();
}

function switchTab(tabName, evt) {
    document.querySelectorAll('.tab-btn').forEach(btn => btn.classList.remove('active'));
    document.querySelectorAll('.module-section').forEach(mod => mod.classList.remove('active'));

    if (evt) evt.target.classList.add('active');
    document.getElementById(`mod-${tabName}`).classList.add('active');
}

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

    products.forEach(p => {
        const category = (p.category || '').toLowerCase();
        const available = isAvailable(p.availabilityStatus);

        const itemHtml = `
            <div class="product-item">
                <div>
                    <strong>${p.productName}</strong>
                    <div style="font-size:12px; color:#64748b;">₱${p.price.toFixed(2)} - ${p.description || 'No description'}</div>
                </div>
                <span style="font-size:11px; padding:3px 8px; border-radius:12px; background:${available ? '#d1fae5' : '#fee2e2'}; color:${available ? '#065f46' : '#991b1b'}; font-weight:700;">
                    ${available ? 'Available' : 'Unavailable'}
                </span>
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

// ---------------------------------------------------------
// 2. INGREDIENTS MODULE
// ---------------------------------------------------------
async function loadIngredients() {
    try {
        const res = await fetch(`${API_URL}/Ingredients`);
        if (!res.ok) return;
        const ingredients = await res.json();
        renderIngredients(ingredients);
    } catch (err) {
        console.error("Error loading ingredients:", err);
    }
}

function renderIngredients(ingredients) {
    const tableBody = document.getElementById("ingredients-table-body");
    if (!tableBody) return;

    if (!ingredients || ingredients.length === 0) {
        tableBody.innerHTML = `<tr><td colspan="5" style="color:#94a3b8;">No ingredients records found.</td></tr>`;
        return;
    }

    tableBody.innerHTML = ingredients.map(ing => `
        <tr>
            <td>${ing.ingredientID || ing.ingredientId || ing.id || '-'}</td>
            <td>${ing.ingredientName || ing.name || '-'}</td>
            <td>${ing.quantityInStock ?? ing.stock ?? 0}</td>
            <td>${ing.unitOfMeasure || ing.unit || 'units'}</td>
            <td>
                <span style="color: ${(ing.quantityInStock <= (ing.reorderLevel || 10)) ? '#dc2626' : '#16a34a'}; font-weight:bold;">
                    ${(ing.quantityInStock <= (ing.reorderLevel || 10)) ? 'Low Stock' : 'Available'}
                </span>
            </td>
        </tr>
    `).join('');
}

// ---------------------------------------------------------
// 3. EMPLOYEES MODULE
// ---------------------------------------------------------
async function loadEmployees() {
    try {
        const res = await fetch(`${API_URL}/Employees`);
        if (!res.ok) return;
        const employees = await res.json();
        renderEmployees(employees);
    } catch (err) {
        console.error("Error loading employees:", err);
    }
}

function renderEmployees(employees) {
    const tableBody = document.getElementById("employees-table-body");
    if (!tableBody) return;

    if (!employees || employees.length === 0) {
        tableBody.innerHTML = `<tr><td colspan="4" style="color:#94a3b8;">No employee records found.</td></tr>`;
        return;
    }

    tableBody.innerHTML = employees.map(emp => {
        const id = emp.employeeID || emp.employeeId || emp.id || '-';

        let name = emp.employeeName || emp.fullName || emp.name;
        if (!name) {
            const fname = emp.firstName || emp.first_name || '';
            const lname = emp.lastName || emp.last_name || '';
            name = (fname || lname) ? `${fname} ${lname}`.trim() : Object.values(emp).find(v => typeof v === 'string' && !/^\d+$/.test(v) && v !== emp.role && v !== emp.position) || '-';
        }

        const role = emp.role || emp.position || emp.jobTitle || emp.employeeRole || 'Staff';
        const phone = emp.contactNumber || emp.contactNum || emp.phoneNumber || emp.phone || emp.contactNo || Object.values(emp).find(v => typeof v === 'string' && /^\d+$/.test(v)) || 'N/A';

        return `
            <tr>
                <td>${id}</td>
                <td>${name}</td>
                <td>${role}</td>
                <td>${phone}</td>
            </tr>
        `;
    }).join('');
}

// ---------------------------------------------------------
// 4. SUPPLIERS MODULE
// ---------------------------------------------------------
async function loadSuppliers() {
    try {
        const res = await fetch(`${API_URL}/Suppliers`);
        if (!res.ok) return;
        const suppliers = await res.json();
        renderSuppliers(suppliers);
    } catch (err) {
        console.error("Error loading suppliers:", err);
    }
}

function renderSuppliers(suppliers) {
    const tableBody = document.getElementById("suppliers-table-body");
    if (!tableBody) return;

    if (!suppliers || suppliers.length === 0) {
        tableBody.innerHTML = `<tr><td colspan="4" style="color:#94a3b8;">No supplier records found.</td></tr>`;
        return;
    }

    tableBody.innerHTML = suppliers.map(sup => {
        const id = sup.supplierID || sup.supplierId || sup.id || '-';
        const name = sup.supplierName || sup.name || Object.values(sup).find(v => typeof v === 'string' && !/^\d+$/.test(v)) || '-';
        const contactPerson = sup.contactPerson || sup.person || 'N/A';
        const phone = sup.contactNumber || sup.contactNum || sup.phoneNumber || sup.phone || 'N/A';

        return `
            <tr>
                <td>${id}</td>
                <td>${name}</td>
                <td>${contactPerson}</td>
                <td>${phone}</td>
            </tr>
        `;
    }).join('');
}