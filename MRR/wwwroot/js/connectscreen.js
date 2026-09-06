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
            "<td><button class='button' id='connbtn" + rid + "' onclick='toggleConnect(" + rid + ");'></button></td>" +
            "<td id='namecell" + rid + "' style='padding:6px; min-width:80px;'>--</td>" +
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
        btn.title = r.ConnectStatusDesc || "Unknown";
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

function toggleMenu() {
    document.getElementById('menuDropdown').classList.toggle('open');
}

function closeMenu() {
    document.getElementById('menuDropdown').classList.remove('open');
}

document.addEventListener('click', function (ev) {
    var menuBar = document.getElementById('menuBar');
    if (menuBar && !menuBar.contains(ev.target)) closeMenu();
});

// connection/reconnect handling lives in js/datahub-connection.js; this listens for
// the payload it dispatches.
document.addEventListener('datapacket', function (ev) {
    datapacket = ev.detail;
    showAll();
});
