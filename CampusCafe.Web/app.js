const API_URL = "http://localhost:5005/api";

let allIngredients = [];
let allEmployees = [];
let allSuppliers = [];

document.addEventListener("DOMContentLoaded", () => {
    fetchAllData();

    document.getElementById("product-form").addEventListener("submit", saveProduct);
    document.getElementById("ingredient-form").addEventListener("submit", saveIngredient);
    document.getElementById("employee-form").addEventListener("submit", saveEmployee);
    document.getElementById("supplier-form").addEventListener("submit", saveSupplier);
});

function switchTab(tabId, evt) {
    document.querySelectorAll('.tab-content').forEach(el => el.classList.remove('active'));
    document.querySelectorAll('.tab-btn').forEach(el => el.classList.remove('active'));
    
    document.getElementById(tabId).classList.add('active');
    evt.target.classList.add('active');
}

async function fetchAllData() {
    await fetchProducts();
    await fetchIngredients();
    await fetchEmployees();
    await fetchSuppliers();
}

// 1. PRODUCTS
async function fetchProducts() {
    try {
        const res = await fetch(`${API_URL}/Products`);
        if (!res.ok) throw new Error("API network error");
        const products = await res.json();
        renderProducts(products);
    } catch (err) {
        console.error("Failed to load products from API:", err);
    }
}

function renderProducts(products) {
    if (!products || !Array.isArray(products)) return;
    const filterCat = (cat) => products.filter(p => p.category && p.category.toLowerCase().includes(cat.toLowerCase()));

    renderProductCategory('coffee-list', filterCat('Coffee').filter(p => !p.category.toLowerCase().includes('non')));
    renderProductCategory('non-coffee-list', filterCat('Non'));
    renderProductCategory('milktea-list', filterCat('Tea'));
    renderProductCategory('chocolate-list', filterCat('Chocolate'));
}

function renderProductCategory(elementId, items) {
    const container = document.getElementById(elementId);
    if (!container) return;

    if (items.length === 0) {
        container.innerHTML = `<div style="font-size:12px; color:#94a3b8; padding:8px;">No products found</div>`;
        return;
    }

    container.innerHTML = items.map(p => `
        <div class="item-card">
            <div>
                <strong>${p.productName}</strong>
                <button class="badge ${isAvailable(p.availabilityStatus) ? 'available' : 'unavailable'}" 
                        onclick="toggleStatus(${p.productID}, '${p.availabilityStatus}')">
                    ${p.availabilityStatus}
                </button>
                <div style="font-size:11px; color:#64748b; margin-top:2px;">${p.description || ''}</div>
            </div>
            <strong>₱${p.price ? p.price.toFixed(2) : '0.00'}</strong>
        </div>
    `).join('');
}

function isAvailable(status) {
    if (!status) return true;
    return status.toLowerCase() === 'available' || status.toLowerCase() === 'true';
}

async function toggleStatus(id, currentStatus) {
    const newStatus = isAvailable(currentStatus) ? "Unavailable" : "Available";
    try {
        await fetch(`${API_URL}/Products/${id}/toggle-status`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ availabilityStatus: newStatus })
        });
        fetchProducts();
    } catch (err) {
        alert("Failed to update status. Make sure C# API is running!");
    }
}

async function saveProduct(e) {
    e.preventDefault();
    const body = {
        productName: document.getElementById('p-name').value,
        category: document.getElementById('p-category').value,
        price: parseFloat(document.getElementById('p-price').value),
        description: document.getElementById('p-desc').value,
        availabilityStatus: document.getElementById('p-available').checked ? "Available" : "Unavailable"
    };

    try {
        await fetch(`${API_URL}/Products`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        document.getElementById('product-form').reset();
        fetchProducts();
    } catch (err) {
        alert("Could not save product. Verify C# API terminal is running.");
    }
}

// 2. INGREDIENTS
async function fetchIngredients() {
    try {
        const res = await fetch(`${API_URL}/Ingredients`);
        if (!res.ok) throw new Error("API network error");
        allIngredients = await res.json();
        renderIngredients(allIngredients);
    } catch (err) {
        console.error("Failed to load ingredients:", err);
    }
}

function renderIngredients(items) {
    const container = document.getElementById('ingredients-list');
    if (!container) return;

    if (!items || items.length === 0) {
        container.innerHTML = `<div style="padding:12px; color:#94a3b8;">No ingredients found</div>`;
        return;
    }

    container.innerHTML = items.map(i => `
        <div class="item-card">
            <div>
                <strong>${i.ingredientName}</strong>
                <div style="font-size:12px; color:#64748b; margin-top:2px;">Quantity: <b style="color:#2563eb;">${i.stockQuantity}</b> ${i.unit || 'pcs'}</div>
            </div>
            <div>
                <button class="btn-qty btn-minus" onclick="updateStock(${i.ingredientID}, ${i.stockQuantity - 1})">−</button>
                <button class="btn-qty btn-plus" onclick="updateStock(${i.ingredientID}, ${i.stockQuantity + 1})">+</button>
            </div>
        </div>
    `).join('');
}

async function updateStock(id, newQty) {
    if (newQty < 0) return;
    try {
        await fetch(`${API_URL}/Ingredients/${id}/update-stock`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ newQuantity: newQty })
        });
        fetchIngredients();
    } catch (err) {
        alert("Stock update failed. Verify API server.");
    }
}

async function saveIngredient(e) {
    e.preventDefault();
    const body = {
        ingredientName: document.getElementById('i-name').value,
        stockQuantity: parseInt(document.getElementById('i-qty').value),
        unit: document.getElementById('i-unit').value
    };

    try {
        await fetch(`${API_URL}/Ingredients`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        document.getElementById('ingredient-form').reset();
        fetchIngredients();
    } catch (err) {
        alert("Failed to save ingredient.");
    }
}

function filterIngredients() {
    const filter = document.getElementById('search-ingredient').value.toLowerCase();
    renderIngredients(allIngredients.filter(i => i.ingredientName && i.ingredientName.toLowerCase().includes(filter)));
}

// 3. EMPLOYEES
async function fetchEmployees() {
    try {
        const res = await fetch(`${API_URL}/Employees`);
        if (!res.ok) throw new Error("API network error");
        allEmployees = await res.json();
        renderEmployees(allEmployees);
    } catch (err) {
        console.error("Failed to load employees:", err);
    }
}

function renderEmployees(items) {
    const container = document.getElementById('employees-list');
    if (!container) return;

    if (!items || items.length === 0) {
        container.innerHTML = `<div style="padding:12px; color:#94a3b8;">No employees found</div>`;
        return;
    }

    container.innerHTML = items.map(e => `
        <div class="item-card">
            <div>
                <strong>${e.name}</strong>
                <div style="font-size:12px; color:#64748b; margin-top:2px;">
                    Contact: ${e.contactNumber || 'N/A'} | Schedule: <span style="color:#16a34a; font-weight:bold;">${e.workSchedule || 'Full-Time'}</span>
                </div>
            </div>
        </div>
    `).join('');
}

async function saveEmployee(evt) {
    evt.preventDefault();
    const body = {
        name: document.getElementById('e-name').value,
        contactNumber: document.getElementById('e-phone').value,
        workSchedule: document.getElementById('e-schedule').value || "Full-Time"
    };

    try {
        await fetch(`${API_URL}/Employees`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        document.getElementById('employee-form').reset();
        fetchEmployees();
    } catch (err) {
        alert("Failed to save employee.");
    }
}

function filterEmployees() {
    const filter = document.getElementById('search-employee').value.toLowerCase();
    renderEmployees(allEmployees.filter(e => e.name && e.name.toLowerCase().includes(filter)));
}

// 4. SUPPLIERS
async function fetchSuppliers() {
    try {
        const res = await fetch(`${API_URL}/Suppliers`);
        if (!res.ok) throw new Error("API network error");
        allSuppliers = await res.json();
        renderSuppliers(allSuppliers);
    } catch (err) {
        console.error("Failed to load suppliers:", err);
    }
}

function renderSuppliers(items) {
    const container = document.getElementById('suppliers-list');
    if (!container) return;

    if (!items || items.length === 0) {
        container.innerHTML = `<div style="padding:12px; color:#94a3b8;">No suppliers found</div>`;
        return;
    }

    container.innerHTML = items.map(s => `
        <div class="item-card">
            <div>
                <strong>${s.supplierName}</strong>
                <div style="font-size:12px; color:#64748b; margin-top:2px;">
                    Contact Person: ${s.contactPerson || 'N/A'} | Phone: <span style="color:#2563eb; font-weight:bold;">${s.phone || 'N/A'}</span>
                </div>
            </div>
        </div>
    `).join('');
}

async function saveSupplier(evt) {
    evt.preventDefault();
    const body = {
        supplierName: document.getElementById('s-name').value,
        contactPerson: document.getElementById('s-person').value,
        phone: document.getElementById('s-phone').value
    };

    try {
        await fetch(`${API_URL}/Suppliers`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        document.getElementById('supplier-form').reset();
        fetchSuppliers();
    } catch (err) {
        alert("Failed to save supplier.");
    }
}

function filterSuppliers() {
    const filter = document.getElementById('search-supplier').value.toLowerCase();
    renderSuppliers(allSuppliers.filter(s => s.supplierName && s.supplierName.toLowerCase().includes(filter)));
}