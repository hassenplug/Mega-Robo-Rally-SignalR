var datapacket = null;
var editMode = false;
var lastRobotCount = 0;

// Connected / Connecting map to a "disconnect" action; everything else tries to connect.
var CONNECT_STATUS_CONNECTED = 22;
var CONNECT_STATUS_CONNECTING = 21;

function buildRows(robots) {
    if (robots.length === lastRobotCount) return;
    lastRobotCount = robots.length;

    var html = '';
    for (var i = 0; i < robots.length; i++) {
        var rid = robots[i].RobotID;
        html += "<tr id='row" + rid + "'>" +
            "<td><button class='button' id='connbtn" + rid + "' onclick='toggleConnect(" + rid + ");'>--</button></td>" +
            "<td id='namecell" + rid + "' style='padding:6px; min-width:160px;'>--</td>" +
            "</tr>";
    }
    document.getElementById("robotRows").innerHTML = html;
}

function showAll() {
    var robots = datapacket.robots;
    buildRows(robots);

    for (var i = 0; i < robots.length; i++) {
        var r = robots[i];

        var btn = document.getElementById("connbtn" + r.RobotID);
        btn.style.backgroundColor = "#" + r.ConnectStatusColor;
        btn.textContent = r.ConnectStatusDesc || "Unknown";
        btn.dataset.status = r.ConnectStatusID;

        var cell = document.getElementById("namecell" + r.RobotID);
        cell.style.backgroundColor = "#" + r.RobotColor;
        cell.style.color = "#" + r.RobotColorFG;

        if (editMode) {
            var input = cell.querySelector('input');
            if (!input) {
                cell.innerHTML = "<input type='text' style='width:90%;' placeholder='IP address'/>";
                input = cell.querySelector('input');
                input.addEventListener('keydown', function (ev) {
                    if (ev.key === 'Enter') saveIp(this);
                });
                input.addEventListener('blur', function () { saveIp(this); });
            }
            input.dataset.robotId = r.RobotID;
            if (document.activeElement !== input) input.value = r.IPAddress || '';
        } else {
            cell.innerHTML = '';
            cell.textContent = r.RobotName;
        }
    }
}

function saveIp(input) {
    var robotId = input.dataset.robotId;
    var ip = input.value.trim();
    if (!ip) return;
    fetch('/api/robot/setip/' + robotId + '/' + encodeURIComponent(ip))
        .then(function (resp) {
            if (!resp.ok) return resp.json().then(function (e) { alert(e.error || 'Failed to set IP'); });
        })
        .catch(function (err) { console.error(err); });
}

function toggleConnect(robotId) {
    var btn = document.getElementById('connbtn' + robotId);
    var status = Number(btn.dataset.status);
    var action = (status === CONNECT_STATUS_CONNECTED || status === CONNECT_STATUS_CONNECTING) ? 'disconnect' : 'connect';
    fetch('/api/robot/' + action + '/' + robotId).catch(function (err) { console.error(err); });
}

function connectAll() {
    fetch('/api/robot/connect/all').catch(function (err) { console.error(err); });
}

function disconnectAll() {
    fetch('/api/robot/disconnect/all').catch(function (err) { console.error(err); });
}

function searchRobots() {
    var btn = document.getElementById('btnSearch');
    btn.disabled = true;
    var originalText = btn.textContent;
    btn.textContent = 'Searching...';

    fetch('/api/robot/search')
        .then(function (resp) { return resp.json(); })
        .then(function (data) {
            var found = data.found || [];
            if (found.length === 0) {
                alert('No AIM robots found responding on the game LAN.');
                return;
            }
            var lines = found.map(function (d) {
                return d.matchedRobotID
                    ? d.ipAddress + '  ->  already assigned to robot ' + d.matchedRobotID
                    : d.ipAddress + '  ->  unassigned AIM robot; use Update IP to assign it to a robot';
            });
            alert('Search results:\n' + lines.join('\n'));
        })
        .catch(function (err) { alert('Search failed: ' + err); })
        .finally(function () {
            btn.disabled = false;
            btn.textContent = originalText;
        });
}

function toggleEditMode() {
    editMode = !editMode;
    document.getElementById('btnUpdateIp').textContent = editMode ? 'Done Editing IPs' : 'Update IP';
    if (datapacket) showAll();
}

// signalR part with automatic reconnect -- same subscription index.html uses (js/loadrobots.js)
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/datahub")
    .withAutomaticReconnect()
    .build();

connection.on("AllDataUpdate", function (data) {
    if (typeof data === 'string') {
        try {
            data = JSON.parse(data);
        } catch (err) {
            console.error('Failed to parse AllDataUpdate payload as JSON', err, data);
            return;
        }
    }
    datapacket = data;
    showAll();
});

connection.onreconnecting(function (error) {
    console.warn('SignalR connection lost. Reconnecting...', error);
});

connection.onreconnected(function (connectionId) {
    console.log('SignalR reconnected. ConnectionId:', connectionId);
});

connection.onclose(function (error) {
    console.error('SignalR connection closed.', error);
    setTimeout(function () { startConnection(); }, 2000);
});

function startConnection() {
    connection.start().then(function () {
        console.log("SignalR Connected!");
        fetch('/api/alldata').catch(function (err) { console.error('Initial data fetch failed', err); });
    }).catch(function (err) {
        console.error('SignalR failed to connect, retrying in 2s', err.toString());
        setTimeout(function () { startConnection(); }, 2000);
    });
}

startConnection();
