const API_URL = "http://localhost:5005/api";

let allProducts = [];
let cart = [];
let currentCategory = "All";
let currentCustomerName = "";
let currentCustomerType = "Student";

document.addEventListener("DOMContentLoaded", () => {
    loadProducts();
});

function showLandingPage() {
    document.getElementById("landing-hero").style.display = "flex";
    document.getElementById("menu-section").style.display = "none";
}

function showMenuDirectly() {
    if (!currentCustomerName) {
        promptCustomerName();
    } else {
        document.getElementById("landing-hero").style.display = "none";
        document.getElementById("menu-section").style.display = "grid";
    }
}

function startOrderingProcess() {
    promptCustomerName();
}

function promptCustomerName() {
    document.getElementById("name-modal").style.display = "flex";
}

function closeNameModal() {
    document.getElementById("name-modal").style.display = "none";
}

function confirmCustomerName() {
    const nameInput = document.getElementById("customer-name-input").value.trim();
    const typeInput = document.getElementById("customer-type-input").value;

    if (!nameInput) {
        alert("Please enter a valid name to continue.");
        return;
    }

    currentCustomerName = nameInput;
    currentCustomerType = typeInput;

    document.getElementById("customer-display-name").textContent = `${currentCustomerName} (${currentCustomerType})`;
    document.getElementById("name-modal").style.display = "none";
    
    document.getElementById("landing-hero").style.display = "none";
    document.getElementById("menu-section").style.display = "grid";
}

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
    if (status === null || status === undefined) return true;
    const s = String(status).trim().toLowerCase();
    return s !== 'unavailable' && s !== 'false' && s !== '0';
}

function filterCategory(category, evt) {
    currentCategory = category;
    document.querySelectorAll('.cat-btn').forEach(btn => btn.classList.remove('active'));
    if (evt) evt.target.classList.add('active');
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

    grid.innerHTML = filtered.map(p => {
        const imagePath = getProductImagePath(p.productName);
        const bgClass = getCategoryBgClass(p.productName, p.category);

        const imageHtml = imagePath 
            ? `<img src="${imagePath}" alt="${p.productName}" style="width: 100%; height: 100%; object-fit: cover; border-radius: 12px;" onerror="this.onerror=null; this.parentNode.innerHTML='${getDrinkIcon(p.category)}';">`
            : getDrinkIcon(p.category);

        return `
            <div class="product-card" onclick="openProductModal(${p.productID})">
                <div class="image-placeholder ${bgClass}">
                    ${imageHtml}
                </div>
                <div>
                    <div class="product-title">${p.productName}</div>
                    <div class="product-price">₱${p.price.toFixed(2)}</div>
                </div>
            </div>
        `;
    }).join('');
}

function getProductImagePath(productName) {
    const name = productName.toLowerCase();

    if (name.includes('cappuccino') || name.includes('cappucino')) return 'images/Cappucino.jpg';
    if (name.includes('caramel')) return 'images/Caramel Macchiato.jpg';
    if (name.includes('mocha')) return 'images/Iced Mocha.jpg';
    if (name.includes('spanish')) return 'images/Iced-Spanish-Latte.jpg';
    if (name.includes('decaf') || name.includes('decaffeinated')) return 'images/Decaf.jpg';

    if (name.includes('matcha')) return 'images/Matcha Latte.jpg';
    if (name.includes('strawberry')) return 'images/Strawberry Latte.jpg';
    if (name.includes('taro')) return 'images/Taro Milk Drink.jpg';
    if (name.includes('mango') || name.includes('refresca') || name.includes('passionfruit')) return 'images/Mango Passionfruit....jpg';

    if (name.includes('hojicha')) return 'images/Hojicha.jpg';
    if (name.includes('oreo')) return 'images/Oreo cake.jpg';
    if (name.includes('black sugar')) return 'images/Black Sugar.jpg';
    if (name.includes('blueberry')) return 'images/Blueberry.jpg';
    if (name.includes('cake')) return 'images/Cake Milk Tea.jpg';

    if (name.includes('fudge') || name.includes('cocoa') || name.includes('shake')) return 'images/Double fudge coco....jpg';
    if (name.includes('hazelnut')) return 'images/Hazelnut Hot Choco.jpg';
    if (name.includes('mint')) return 'images/Iced mint dark choco.jpg';
    if (name.includes('dark chocolate') || name.includes('dark')) return 'images/Dark Chocolate.jpg';

    return ''; 
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

function openProductModal(productId) {
    const product = allProducts.find(p => p.productID === productId);
    if (!product) return;

    const imagePath = getProductImagePath(product.productName);
    const modalImg = document.getElementById("modal-img");

    modalImg.className = `modal-image ${getCategoryBgClass(product.productName, product.category)}`;
    
    if (imagePath) {
        modalImg.innerHTML = `
            <img src="${imagePath}" 
                 alt="${product.productName}" 
                 style="width: 100%; height: 100%; object-fit: cover; border-radius: 12px;"
                 onerror="this.onerror=null; this.parentNode.innerHTML='${getDrinkIcon(product.category)}';">
        `;
    } else {
        modalImg.textContent = getDrinkIcon(product.category);
    }

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
        cartBody.innerHTML = `
            <div class="empty-cart-state">
                <div class="empty-cart-icon">🛍️</div>
                <div class="empty-cart-title">Your order is empty</div>
                <div class="empty-cart-sub">Select beverages from the menu to build your order</div>
            </div>
        `;
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
    if (!currentCustomerName) {
        promptCustomerName();
        return;
    }

    if (cart.length === 0) {
        alert("Please add at least one product before checking out.");
        return;
    }

    const subtotal = cart.reduce((sum, item) => sum + (item.price * item.quantity), 0);
    const totalAmount = subtotal * 1.12;

    const payload = {
        customerName: currentCustomerName,
        customerType: currentCustomerType,
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
            
            document.getElementById("success-order-id").textContent = `#${result.orderId}`;
            document.getElementById("success-cust-name").textContent = `${result.customerName} (${currentCustomerType})`;
            document.getElementById("success-item-count").textContent = `${cart.reduce((a, b) => a + b.quantity, 0)} items`;
            document.getElementById("success-total-amount").textContent = `₱${totalAmount.toFixed(2)}`;

            document.getElementById("success-modal").style.display = "flex";

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

function closeSuccessModal() {
    document.getElementById("success-modal").style.display = "none";
}

function showAboutModal() {
    document.getElementById("info-modal-title").textContent = "About Campus Café ☕";
    document.getElementById("info-modal-body").innerHTML = `
        Campus Café is your ultimate study fuel spot on campus! We serve handcrafted espresso beverages, iced teas, and signature specialty drinks prepared fresh every day.<br><br>
        <strong>Location:</strong> Student Activity Center, Ground Floor<br>
        <strong>Hours:</strong> Mon - Sat: 7:00 AM - 8:00 PM
    `;
    document.getElementById("info-modal").style.display = "flex";
}

function showContactModal() {
    document.getElementById("info-modal-title").textContent = "Contact Us 📞";
    document.getElementById("info-modal-body").innerHTML = `
        Need assistance with a bulk order or catering event?<br><br>
        • <strong>Phone:</strong> (032) 123-4567 / 0917-123-4567<br>
        • <strong>Email:</strong> support@campuscafe.edu.ph<br>
        • <strong>Facebook / Instagram:</strong> @CampusCafeOfficial
    `;
    document.getElementById("info-modal").style.display = "flex";
}

function closeInfoModal(evt) {
    if (!evt || evt.target.id === "info-modal" || evt.target.classList.contains("modal-close")) {
        document.getElementById("info-modal").style.display = "none";
    }
}