const connection = new signalR.HubConnectionBuilder()
    .withUrl("http://localhost:5106/controlhub")
    .withAutomaticReconnect()
    .build();

const logOutput = document.getElementById('log-output');

// --- HÀM NGẮT CAM ---
function stopWebcamUI() {
    sendCommand('WEBCAM_OFF');
    const img = document.getElementById('webcam-feed');
    const status = document.getElementById('camStatus');
    if (img) { img.src = ""; img.style.display = 'none'; }
    if (status) { status.style.display = 'block'; }
    logOutput.value += "[SYSTEM] Camera Stopped.\n";
    logOutput.scrollTop = logOutput.scrollHeight;
}

connection.on("ReceiveWebcam", (base64) => {
    const img = document.getElementById('webcam-feed');
    const status = document.getElementById('camStatus');
    if (img) {
        img.src = "data:image/jpeg;base64," + base64;
        img.style.display = 'block';
        if (status) status.style.display = 'none';
    }
});

connection.on("ReceiveLog", (message) => {
    logOutput.value += `${message}\n`;
    logOutput.scrollTop = logOutput.scrollHeight;
});

// --- XỬ LÝ DROPDOWN THÔNG MINH ---
connection.on("ClientConnected", (id) => {
    const sel = document.getElementById('target-select');

    // 1. Nếu đang có dòng "Waiting...", xóa nó đi ngay
    const waitingOption = sel.querySelector('option[value=""]');
    if (waitingOption) {
        waitingOption.remove();
    }

    // 2. Thêm Target mới vào
    if (!document.getElementById(`opt-${id}`)) {
        const opt = document.createElement('option');
        opt.value = id;
        opt.id = `opt-${id}`;
        opt.textContent = `TARGET: ${id}`; // Chỉ hiện tên Target
        sel.appendChild(opt);

        // Tự động chọn luôn target mới này
        sel.value = id;

        logOutput.value += `>>> CONNECTION ESTABLISHED: ${id}\n`;
    }
});

connection.on("ClientDisconnected", (id) => {
    const opt = document.getElementById(`opt-${id}`);
    if (opt) opt.remove();
    logOutput.value += `<<< CONNECTION LOST: ${id}\n`;

    // 3. Nếu danh sách trống trơn, thêm lại dòng Waiting
    const sel = document.getElementById('target-select');
    if (sel.options.length === 0) {
        const defaultOpt = document.createElement('option');
        defaultOpt.value = "";
        defaultOpt.textContent = "-- WAITING FOR CONNECTION --";
        sel.appendChild(defaultOpt);
    }
});

// GỬI LỆNH
function sendCommand(cmd) {
    const sel = document.getElementById('target-select');
    // Kiểm tra kỹ: Phải chọn Target và Target đó không phải là rỗng
    if (sel && sel.value && sel.value !== "") {
        connection.invoke("SendCommand", sel.value, cmd).catch(err => {
            console.error(err);
        });
    } else {
        alert("NO TARGET SELECTED!");
    }
}

function sendAppCommand(act) {
    const v = document.getElementById('pid').value;
    if (v) sendCommand(`${act}|${v}`);
    else alert("Please enter App Name or PID.");
}

function confirmPower(c) {
    if (confirm("Confirm execution?")) sendCommand(c);
}

connection.start().then(() => {
    logOutput.value += "--- SERVER LISTENING (PORT 9999) ---\n";
});