const state = {
    status: null,
    activePanel: "powershell",
    transactions: []
};

const $ = (id) => document.getElementById(id);

const taskBuilders = {
    powershell: () => ({
        taskType: $("psType").value,
        payload: {
            script: $("psScript").value,
            timeoutSeconds: numberValue("psTimeout", 30)
        }
    }),
    service: () => ({
        taskType: $("serviceTask").value,
        payload: {
            serviceName: $("serviceName").value,
            timeoutSeconds: numberValue("serviceTimeout", 60)
        }
    }),
    folder: () => buildFolderTask(),
    deploy: () => ({
        taskType: $("deployTask").value,
        payload: {
            fileUrl: $("deployFileUrl").value,
            timeoutSeconds: numberValue("deployTimeout", 300),
            cleanTargetBeforeExtract: $("deployCleanBeforeExtract").checked
        }
    }),
    iis: () => ({
        taskType: $("iisTask").value,
        payload: {
            timeoutSeconds: numberValue("iisTimeout", 120)
        }
    })
};

document.addEventListener("DOMContentLoaded", async () => {
    bindNavigation();
    bindActions();
    await loadStatus();
    updateRawPreview();
});

function bindNavigation() {
    document.querySelectorAll(".nav").forEach((button) => {
        button.addEventListener("click", () => {
            state.activePanel = button.dataset.panel;
            document.querySelectorAll(".nav").forEach((item) => item.classList.toggle("active", item === button));
            document.querySelectorAll(".panel").forEach((panel) => panel.classList.toggle("active", panel.id === `panel-${state.activePanel}`));
            updateRawPreview();
        });
    });
}

function bindActions() {
    $("runPowerShell").addEventListener("click", () => runTask(taskBuilders.powershell()));
    $("runService").addEventListener("click", () => runTask(taskBuilders.service()));
    $("runFolder").addEventListener("click", () => runTask(taskBuilders.folder()));
    $("runDeploy").addEventListener("click", () => runTask(taskBuilders.deploy()));
    $("runIis").addEventListener("click", () => runTask(taskBuilders.iis()));
    $("runPrintImage").addEventListener("click", printImage);
    $("refreshTransactions").addEventListener("click", loadTransactions);
    $("printTransaction").addEventListener("change", renderTransactionPreview);
    $("runRaw").addEventListener("click", () => runTask(JSON.parse($("rawJson").value)));
    $("refreshReceived").addEventListener("click", loadReceivedTasks);
    $("clearOutput").addEventListener("click", () => setOutput("Ready."));

    document.querySelectorAll("input, select, textarea").forEach((field) => {
        field.addEventListener("input", updateRawPreview);
        field.addEventListener("change", updateRawPreview);
    });
}

async function loadStatus() {
    try {
        const response = await fetch("/api/local/status");
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        state.status = await response.json();
        $("statusDot").className = "dot ok";
        $("statusText").textContent = "Online";
        $("agentMeta").textContent = `${state.status.machineCode} | ${state.status.agentVersion} | ${location.origin}`;

        fillSelect("serviceName", state.status.managedServices ?? []);
        fillSelect("folderKey", Object.keys(state.status.managedFolders ?? {}));
    } catch (error) {
        $("statusDot").className = "dot fail";
        $("statusText").textContent = "Offline";
        $("agentMeta").textContent = error.message;
    }
}

function fillSelect(id, values) {
    const select = $(id);
    select.innerHTML = "";

    for (const value of values) {
        const option = document.createElement("option");
        option.value = value;
        option.textContent = value;
        select.appendChild(option);
    }
}

function buildFolderTask() {
    const taskType = $("folderTask").value;
    const folderKey = $("folderKey").value;
    const relativePath = $("folderRelativePath").value;
    const timeoutSeconds = numberValue("folderTimeout", 300);

    if (taskType === "GET_FOLDER_INFO") {
        return { taskType, payload: { folderKey } };
    }

    if (taskType === "LIST_FOLDER_FILES") {
        return {
            taskType,
            payload: {
                folderKey,
                relativePath,
                recursive: $("folderRecursive").checked
            }
        };
    }

    if (taskType === "DOWNLOAD_TO_FOLDER") {
        return {
            taskType,
            payload: {
                folderKey,
                fileUrl: $("folderFileUrl").value,
                fileName: $("folderFileName").value,
                overwrite: $("folderOverwrite").checked,
                timeoutSeconds
            }
        };
    }

    if (taskType === "EXTRACT_ZIP_TO_FOLDER") {
        return {
            taskType,
            payload: {
                folderKey,
                fileUrl: $("folderFileUrl").value,
                overwrite: $("folderOverwrite").checked,
                cleanTargetBeforeExtract: $("folderCleanBeforeExtract").checked,
                timeoutSeconds
            }
        };
    }

    if (taskType === "DELETE_FOLDER_FILE") {
        return {
            taskType,
            payload: {
                folderKey,
                relativePath
            }
        };
    }

    return {
        taskType,
        payload: { folderKey }
    };
}

async function runTask(task) {
    setOutput("Running...");

    try {
        const response = await fetch("/api/local/tasks/run", {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify(task)
        });

        const text = await response.text();
        const data = text ? JSON.parse(text) : {};

        if (!response.ok) {
            throw new Error(JSON.stringify(data, null, 2) || `HTTP ${response.status}`);
        }

        setOutput(JSON.stringify(data, null, 2));
    } catch (error) {
        setOutput(error.stack || error.message);
    }
}

function updateRawPreview() {
    if (state.activePanel === "raw") {
        return;
    }

    const builder = taskBuilders[state.activePanel];
    if (!builder) {
        if (state.activePanel === "received") {
            loadReceivedTasks();
        }

        if (state.activePanel === "print") {
            loadTransactions();
        }

        return;
    }

    $("rawJson").value = JSON.stringify(builder(), null, 2);
}

function numberValue(id, fallback) {
    const value = Number($(id).value);
    return Number.isFinite(value) && value > 0 ? value : fallback;
}

function setOutput(value) {
    $("outputText").textContent = value;
}

async function loadReceivedTasks() {
    const list = $("receivedList");
    list.textContent = "Loading...";

    try {
        const response = await fetch("/api/local/received-tasks");
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        const items = await response.json();
        list.innerHTML = "";

        if (!items.length) {
            list.textContent = "No received tasks yet.";
            return;
        }

        for (const item of items) {
            const wrapper = document.createElement("div");
            wrapper.className = "received-item";

            const title = document.createElement("strong");
            title.textContent = `${item.taskType} | ${item.taskId}`;

            const meta = document.createElement("small");
            meta.textContent = `${item.source} | ${item.receivedAt}`;

            const raw = document.createElement("pre");
            raw.textContent = item.rawJson || JSON.stringify(item, null, 2);

            wrapper.append(title, meta, raw);
            list.appendChild(wrapper);
        }
    } catch (error) {
        list.textContent = error.stack || error.message;
    }
}

async function loadTransactions() {
    const select = $("printTransaction");
    const preview = $("transactionPreview");
    select.innerHTML = "";
    preview.textContent = "Loading transactions...";

    try {
        const response = await fetch("/api/local/transactions");
        const text = await response.text();
        const data = text ? JSON.parse(text) : [];

        if (!response.ok) {
            throw new Error(JSON.stringify(data, null, 2) || `HTTP ${response.status}`);
        }

        state.transactions = data;

        if (!state.transactions.length) {
            preview.textContent = "No transactions found.";
            return;
        }

        for (const item of state.transactions) {
            const option = document.createElement("option");
            option.value = item.transactionId;
            option.textContent = buildTransactionLabel(item);
            select.appendChild(option);
        }

        renderTransactionsTable();
    } catch (error) {
        preview.textContent = error.stack || error.message;
    }
}

function renderTransactionPreview() {
    const selectedId = $("printTransaction").value;
    document.querySelectorAll(".transaction-table tbody tr").forEach((row) => {
        row.classList.toggle("selected", row.dataset.transactionId === selectedId);
    });
}

function buildTransactionLabel(item) {
    const values = item.values ?? {};
    const parts = [
        item.transactionId,
        values.Code,
        values.TransactionCode,
        values.CreatedDate,
        values.CreatedAt
    ].filter(Boolean);

    return parts.length ? parts.join(" | ") : JSON.stringify(values);
}

async function printImage() {
    const payload = {
        transactionId: $("printTransaction").value,
        layoutId: numberValue("printLayoutId", 0),
        numberOfImage: numberValue("printNumberOfImage", 1)
    };

    if (!payload.transactionId) {
        setOutput("Please select a transaction.");
        return;
    }

    setOutput("Printing image...");

    try {
        const response = await fetch("/api/local/print-image", {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify(payload)
        });

        const text = await response.text();
        const data = text ? JSON.parse(text) : {};

        if (!response.ok) {
            throw new Error(JSON.stringify(data, null, 2) || `HTTP ${response.status}`);
        }

        setOutput(JSON.stringify(data, null, 2));
    } catch (error) {
        setOutput(error.stack || error.message);
    }
}

function renderTransactionsTable() {
    const preview = $("transactionPreview");
    preview.innerHTML = "";

    const columns = Array.from(new Set(
        state.transactions.flatMap((item) => Object.keys(item.values ?? {}))
    ));

    if (!columns.length) {
        preview.textContent = "No transaction columns found.";
        return;
    }

    const table = document.createElement("table");
    table.className = "transaction-table";

    const thead = document.createElement("thead");
    const headRow = document.createElement("tr");
    for (const column of columns) {
        const th = document.createElement("th");
        th.textContent = column;
        headRow.appendChild(th);
    }
    thead.appendChild(headRow);

    const tbody = document.createElement("tbody");
    for (const item of state.transactions) {
        const tr = document.createElement("tr");
        tr.dataset.transactionId = item.transactionId;
        tr.addEventListener("click", () => {
            $("printTransaction").value = item.transactionId;
            renderTransactionPreview();
        });

        for (const column of columns) {
            const td = document.createElement("td");
            td.textContent = formatCellValue(item.values?.[column]);
            tr.appendChild(td);
        }

        tbody.appendChild(tr);
    }

    table.append(thead, tbody);
    preview.appendChild(table);
    renderTransactionPreview();
}

function formatCellValue(value) {
    if (value === null || value === undefined) {
        return "";
    }

    if (typeof value === "object") {
        return JSON.stringify(value);
    }

    return String(value);
}
