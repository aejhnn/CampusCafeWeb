/* ============================================================================
   card.js  -  shared "Pay by Card" form for the Customer Kiosk and the
   Management Board.

   Usage:
     1. Put an empty  <div id="PREFIX-card-box"></div>  where the form should appear.
     2. CardForm.mount("PREFIX")            -> builds the form inside that div (once)
     3. CardForm.toggle("PREFIX", true/false) -> show / hide it
     4. CardForm.validate("PREFIX")         -> { ok, error, field, type, short, last4, holder }
     5. CardForm.reset("PREFIX")            -> clears the fields

   SECURITY NOTE: the full card number, expiry and CVV are only checked in the
   browser. They are NEVER sent to the server or saved in the database - only the
   card type (e.g. Visa) and the last 4 digits are used (for the receipt).
   ========================================================================== */
const CardForm = (() => {
    // short = text saved in the Payment table (PaymentMethod column is TEXT(20), e.g. "Card (Mastercard)")
    const TYPES = [
        { value: "Visa",             label: "Visa",             short: "Visa",      network: "visa" },
        { value: "Mastercard",       label: "Mastercard",       short: "Mastercard", network: "mastercard" },
        { value: "JCB",              label: "JCB",              short: "JCB",       network: "jcb" },
        { value: "American Express", label: "American Express", short: "Amex",      network: "amex" },
        { value: "BDO",              label: "BDO",              short: "BDO",       network: "bank" },
        { value: "BPI",              label: "BPI",              short: "BPI",       network: "bank" },
        { value: "Metrobank",        label: "Metrobank",        short: "Metrobank", network: "bank" },
        { value: "UnionBank",        label: "UnionBank",        short: "UnionBank", network: "bank" }
    ];

    function injectStyles() {
        if (document.getElementById("cardform-styles")) return;
        const css = `
            .cf-box { text-align:left; margin-top:6px; padding:12px; background:#f1f5f9; border:1px solid #e2e8f0; border-radius:12px; }
            .cf-box label { display:block; font-size:12px; font-weight:700; color:#475569; margin:10px 0 4px; }
            .cf-box label:first-child { margin-top:0; }
            .cf-box input, .cf-box select { width:100%; padding:10px 12px; border:1px solid #cbd5e1; border-radius:10px; font-size:14px; outline:none; background:#fff; }
            .cf-box input:focus, .cf-box select:focus { border-color:#253874; }
            .cf-box .cf-bad { border-color:#dc2626 !important; background:#fef2f2; }
            .cf-box .cf-row label:first-child { margin-top:10px; }
            .cf-row { display:grid; grid-template-columns:1fr 1fr; gap:10px; }
            .cf-note { font-size:11px; color:#64748b; margin-top:10px; line-height:1.4; }
        `;
        const s = document.createElement("style");
        s.id = "cardform-styles";
        s.textContent = css;
        document.head.appendChild(s);
    }

    const $ = (prefix, part) => document.getElementById(`${prefix}-pf-${part}`);
    const typeInfo = value => TYPES.find(t => t.value === value) || null;
    const digitsOnly = v => String(v || "").replace(/\D/g, "");

    function maxDigits(prefix) {
        const t = typeInfo($(prefix, "type").value);
        return t && t.network === "amex" ? 15 : 16;
    }

    function mount(prefix) {
        injectStyles();
        const box = document.getElementById(`${prefix}-card-box`);
        if (!box || box.dataset.mounted) return;
        box.dataset.mounted = "1";
        box.classList.add("cf-box");
        box.style.display = "none";
        box.innerHTML = `
            <label>Card Type</label>
            <select id="${prefix}-pf-type" name="pf_kind_x" autocomplete="off" onchange="CardForm.onTypeChange('${prefix}')">
                <option value="">Select card type...</option>
                ${TYPES.map(t => `<option value="${t.value}">${t.label}</option>`).join("")}
            </select>

            <label>Cardholder Name</label>
            <input type="text" id="${prefix}-pf-holder" name="pf_holder_x" data-lpignore="true" data-form-type="other" maxlength="60" autocomplete="off" placeholder="Name as shown on the card">

            <label>Card Number</label>
            <input type="text" id="${prefix}-pf-number" name="pf_number_x" data-lpignore="true" data-form-type="other" inputmode="numeric" autocomplete="off" maxlength="23"
                   placeholder="1234 5678 9012 3456" oninput="CardForm.fmtNumber('${prefix}')">

            <div class="cf-row">
                <div>
                    <label>Expiry Date</label>
                    <input type="text" id="${prefix}-pf-expiry" name="pf_expiry_x" data-lpignore="true" data-form-type="other" inputmode="numeric" autocomplete="off" maxlength="5"
                           placeholder="MM/YY" oninput="CardForm.fmtExpiry('${prefix}')">
                </div>
                <div>
                    <label>CVV / CVC</label>
                    <input type="password" id="${prefix}-pf-cvv" name="pf_cvv_x" data-lpignore="true" data-form-type="other" inputmode="numeric" autocomplete="off" maxlength="4"
                           placeholder="123" oninput="this.value = this.value.replace(/\\D/g, '')">
                </div>
            </div>
            <div class="cf-note">🔒 Card details are only checked on this screen. The full card number and CVV are never saved.</div>
        `;
    }

    function toggle(prefix, show) {
        const box = document.getElementById(`${prefix}-card-box`);
        if (!box) return;
        mount(prefix);
        box.style.display = show ? "block" : "none";
    }

    function reset(prefix) {
        const box = document.getElementById(`${prefix}-card-box`);
        if (!box || !box.dataset.mounted) return;
        ["type", "holder", "number", "expiry", "cvv"].forEach(p => {
            const f = $(prefix, p);
            f.value = "";
            f.classList.remove("cf-bad");
        });
    }

    function onTypeChange(prefix) {
        const t = typeInfo($(prefix, "type").value);
        $(prefix, "cvv").maxLength = t && t.network === "amex" ? 4 : 3;
        $(prefix, "cvv").placeholder = t && t.network === "amex" ? "1234" : "123";
        $(prefix, "cvv").value = "";
        fmtNumber(prefix);
    }

    // digits only, grouped 4-4-4-4 (American Express: 4-6-5)
    function fmtNumber(prefix) {
        const input = $(prefix, "number");
        let d = digitsOnly(input.value).slice(0, maxDigits(prefix));
        const t = typeInfo($(prefix, "type").value);
        let out;
        if (t && t.network === "amex") {
            out = [d.slice(0, 4), d.slice(4, 10), d.slice(10, 15)].filter(Boolean).join(" ");
        } else {
            out = d.replace(/(.{4})/g, "$1 ").trim();
        }
        input.value = out;
    }

    // digits only, auto "/" after the month
    function fmtExpiry(prefix) {
        const input = $(prefix, "expiry");
        const d = digitsOnly(input.value).slice(0, 4);
        input.value = d.length > 2 ? d.slice(0, 2) + "/" + d.slice(2) : d;
    }

    function luhn(num) {
        let sum = 0, dbl = false;
        for (let i = num.length - 1; i >= 0; i--) {
            let n = parseInt(num[i], 10);
            if (dbl) { n *= 2; if (n > 9) n -= 9; }
            sum += n;
            dbl = !dbl;
        }
        return sum % 10 === 0;
    }

    function prefixMatches(network, num) {
        const p2 = parseInt(num.slice(0, 2), 10);
        const p4 = parseInt(num.slice(0, 4), 10);
        switch (network) {
            case "visa": return num[0] === "4";
            case "mastercard": return (p2 >= 51 && p2 <= 55) || (p4 >= 2221 && p4 <= 2720);
            case "jcb": return p4 >= 3528 && p4 <= 3589;
            case "amex": return p2 === 34 || p2 === 37;
            default: return true;   // bank-issued cards can run on Visa / Mastercard / JCB
        }
    }

    function markBad(prefix, part) {
        const f = $(prefix, part);
        if (f) { f.classList.add("cf-bad"); f.focus(); }
    }

    function validate(prefix) {
        const box = document.getElementById(`${prefix}-card-box`);
        if (!box || !box.dataset.mounted) return { ok: false, error: "Card form is not ready." };
        ["type", "holder", "number", "expiry", "cvv"].forEach(p => $(prefix, p).classList.remove("cf-bad"));

        const fail = (field, error) => { markBad(prefix, field); return { ok: false, field, error }; };

        const t = typeInfo($(prefix, "type").value);
        if (!t) return fail("type", "Please choose the type of card (e.g. Visa, Mastercard, BDO, BPI).");

        const holder = $(prefix, "holder").value.trim().replace(/\s+/g, " ");
        if (holder.length < 2 || !/^[A-Za-z\u00C0-\u024F][A-Za-z\u00C0-\u024F .,'-]*$/.test(holder))
            return fail("holder", "Please enter the cardholder name as shown on the card (letters only).");

        const num = digitsOnly($(prefix, "number").value);
        const need = t.network === "amex" ? 15 : 16;
        if (num.length !== need)
            return fail("number", `A ${t.label} card number must have ${need} digits.`);
        // NOTE: no real-card check (checksum / starting digits) on purpose - any made-up number is accepted.

        const exp = $(prefix, "expiry").value.trim();
        const m = /^(\d{2})\/(\d{2})$/.exec(exp);
        if (!m) return fail("expiry", "Please enter the expiry date as MM/YY.");
        const month = parseInt(m[1], 10), year = 2000 + parseInt(m[2], 10);
        if (month < 1 || month > 12) return fail("expiry", "The expiry month must be between 01 and 12.");
        const now = new Date();
        if (year < now.getFullYear() || (year === now.getFullYear() && month < now.getMonth() + 1))
            return fail("expiry", "This card has expired.");
        if (year > now.getFullYear() + 20) return fail("expiry", "The expiry year is not valid.");

        const cvvNeed = t.network === "amex" ? 4 : 3;
        const cvv = digitsOnly($(prefix, "cvv").value);
        if (cvv.length !== cvvNeed)
            return fail("cvv", `The CVV / CVC must be ${cvvNeed} digits.`);

        return { ok: true, type: t.value, short: t.short, last4: num.slice(-4), holder };
    }

    // text that is saved in the Payment table, e.g. "Card (Visa)"
    function methodLabel(prefix) {
        const t = typeInfo($(prefix, "type").value);
        return t ? `Card (${t.short})` : "Card";
    }

    return { TYPES, mount, toggle, reset, validate, methodLabel, onTypeChange, fmtNumber, fmtExpiry };
})();
