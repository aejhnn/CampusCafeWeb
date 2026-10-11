const API_URL = "http://localhost:5005/api";

let allProducts = [];
let cart = [];
let currentCategory = "All";
let selectedProductForModal = null;

document.addEventListener("DOMContentLoaded", () => {
    loadProducts();
});

async function loadProducts() {
    try {
        const res = await fetch(`${API_URL}/Products`);
        if (!res.ok) throw new Error("API error");
        const data = await res.json();
        
        allProducts = data.filter(p => isAvailable(p.availabilityStatus));
        renderGrid();
    } catch (err) {
        console.error("Could not load products:", err);
        document.getElementById("products-grid").innerHTML = `<div style="grid-column: 1/-1; color: #ef4444;">Error connecting to API. Please ensure C# backend server is running!</div>`;
    }
}

function isAvailable(status) {
    if (!status) return true;
    const s = String(status).trim().toLowerCase();
    return s === 'available' || s === 'true' || s === '1';
}

function filterCategory(category, evt) {
    currentCategory = category;
    document.querySelectorAll('.cat-btn').forEach(btn => btn.classList.remove('active'));
    evt.target.classList.add('active');
    renderGrid();
}

function searchMenu() {
    renderGrid();
}

function renderGrid() {
    const grid = document.getElementById("products-grid");
    const searchText = document.getElementById("search-input").value.toLowerCase();

    let filtered = allProducts;

    if (currentCategory === 'Coffee') {
        filtered = filtered.filter(p => p.category && p.category.toLowerCase().includes('coffee') && !p.category.toLowerCase().includes('non'));
    } else if (currentCategory === 'Non-Coffee') {
        filtered = filtered.filter(p => p.category && p.category.toLowerCase().includes('non'));
    } else if (currentCategory === 'Tea') {
        filtered = filtered.filter(p => p.category && (p.category.toLowerCase().includes('tea') || p.category.toLowerCase().includes('milk')));
    } else if (currentCategory === 'Chocolate') {
        filtered = filtered.filter(p => p.category && p.category.toLowerCase().includes('chocolate'));
    }

    if (searchText) {
        filtered = filtered.filter(p => p.productName.toLowerCase().includes(searchText));
    }

    if (filtered.length === 0) {
        grid.innerHTML = `<div style="grid-column: 1/-1; color: #9ca3af; font-size: 15px;">No products available.</div>`;
        return;
    }

    grid.innerHTML = filtered.map(p => `
        <div class="product-card" onclick="openProductModal(${p.productID})">
            <div class="image-placeholder ${getCategoryBgClass(p.productName, p.category)}">
                ${getDrinkIcon(p.category)}
            </div>
            <div>
                <div class="product-title">${p.productName}</div>
                <div class="product-price">₱${p.price.toFixed(2)}</div>
            </div>
        </div>
    `).join('');
}

function getCategoryBgClass(name, category) {
    const n = name.toLowerCase();
    const c = (category || '').toLowerCase();

    if (n.includes('matcha')) return 'bg-matcha';
    if (n.includes('strawberry')) return 'bg-strawberry';
    if (n.includes('taro')) return 'bg-taro';
    if (n.includes('refresca') || n.includes('mango')) return 'bg-refresca';
    if (c.includes('chocolate') || n.includes('chocolate') || n.includes('fudge')) return 'bg-chocolate';
    return 'bg-coffee';
}

function getDrinkIcon(category) {
    const c = (category || '').toLowerCase();
    if (c.includes('tea') || c.includes('milk')) return '🧋';
    if (c.includes('chocolate')) return '🍫';
    if (c.includes('non')) return '🥤';
    return '☕';
}

/* MODAL FUNCTIONS */
function openProductModal(productId) {
    const product = allProducts.find(p => p.productID === productId);
    if (!product) return;

    selectedProductForModal = product;

    document.getElementById("modal-img").className = `modal-image ${getCategoryBgClass(product.productName, product.category)}`;
    document.getElementById("modal-img").textContent = getDrinkIcon(product.category);
    document.getElementById("modal-title").textContent = product.productName;
    document.getElementById("modal-desc").textContent = product.description || "Freshly prepared handcrafted beverage.";
    document.getElementById("modal-price").textContent = `₱${product.price.toFixed(2)}`;
    
    document.getElementById("modal-add-btn").onclick = () => {
        addToCart(product.productID);
        closeModal();
    };

    document.getElementById("product-modal").style.display = "flex";
}

function closeModal(evt) {
    if (!evt || evt.target.id === "product-modal" || evt.target.classList.contains("modal-close")) {
        document.getElementById("product-modal").style.display = "none";
    }
}

/* CART ACTIONS */
function addToCart(productId) {
    const product = allProducts.find(p => p.productID === productId);
    if (!product) return;

    const existing = cart.find(item => item.productID === productId);
    if (existing) {
        existing.quantity += 1;
    } else {
        cart.push({
            productID: product.productID,
            productName: product.productName,
            price: product.price,
            quantity: 1
        });
    }

    updateCart();
}

function adjustQty(productId, delta, evt) {
    evt.stopPropagation();
    const item = cart.find(i => i.productID === productId);
    if (!item) return;

    item.quantity += delta;
    if (item.quantity <= 0) {
        cart = cart.filter(i => i.productID !== productId);
    }
    updateCart();
}

function updateCart() {
    const cartBody = document.getElementById("cart-body");

    if (cart.length === 0) {
        cartBody.innerHTML = `<div style="text-align: center; color: #6b7280; margin-top: 40px; font-size: 14px;">Select items from the menu to build your order</div>`;
        document.getElementById("subtotal-val").textContent = "₱0.00";
        document.getElementById("tax-val").textContent = "₱0.00";
        document.getElementById("total-val").textContent = "₱0.00";
        return;
    }

    let subtotal = 0;
    cartBody.innerHTML = cart.map(item => {
        const itemTotal = item.price * item.quantity;
        subtotal += itemTotal;

        return `
            <div class="cart-item">
                <div>${item.productName}</div>
                <div class="cart-item-controls">
                    <button class="cart-qty-btn" onclick="adjustQty(${item.productID}, -1, event)">-</button>
                    <span>${item.quantity}</span>
                    <button class="cart-qty-btn" onclick="adjustQty(${item.productID}, 1, event)">+</button>
                    <span style="margin-left: 8px;">₱${itemTotal.toFixed(2)}</span>
                </div>
            </div>
        `;
    }).join('');

    const tax = subtotal * 0.12;
    const total = subtotal + tax;

    document.getElementById("subtotal-val").textContent = `₱${subtotal.toFixed(2)}`;
    document.getElementById("tax-val").textContent = `₱${tax.toFixed(2)}`;
    document.getElementById("total-val").textContent = `₱${total.toFixed(2)}`;
}

async function payNow() {
    if (cart.length === 0) {
        alert("Please add at least one product before checking out.");
        return;
    }

    const subtotal = cart.reduce((sum, item) => sum + (item.price * item.quantity), 0);
    const totalAmount = subtotal * 1.12;

    const payload = {
        customerID: 1,
        employeeID: 1,
        totalAmount: totalAmount,
        items: cart.map(item => ({
            productID: item.productID,
            quantity: item.quantity,
            unitPrice: item.price
        }))
    };

    try {
        const res = await fetch(`${API_URL}/Orders`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });

        if (res.ok) {
            const result = await res.json();
            alert(`🎉 Order #${result.orderId} placed successfully!`);
            cart = [];
            updateCart();
        } else {
            const err = await res.text();
            alert(`Error processing payment: ${err}`);
        }
    } catch (err) {
        alert("Network error: Please verify C# API server is running.");
    }
}