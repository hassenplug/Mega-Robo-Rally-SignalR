
var CurrentPlayer = 0;
var CurrentLine = 0;
var datapacket = null;
var lastPlayerCount = 0;

// Tap the "F/E/C" column header to spell it out, tap again to abbreviate.
var flagsHeaderExpanded = false;

function toggleFlagsHeader() {
    flagsHeaderExpanded = !flagsHeaderExpanded;
    document.getElementById('flagsHeader').innerText = flagsHeaderExpanded ? 'Flag Energy Cards' : 'F/E/C';
}

// ── Phone login ──────────────────────────────────────────────────────────────
// Closed system: the cookie itself is the identity (a RobotID, or the GM code) --
// no server round trip, no password. It just remembers which physical phone is which
// robot across reloads, and gates the card UI so a phone only ever shows/plays its own
// robot's hand. GM is unrestricted, same as before this existed.
var LOGIN_COOKIE = "mrr_player";
var GM_CODE = "0555";
var IsGM = false;
var LoggedInRobotID = null; // set once the cookie matches a robot in the current game

function getCookie(name) {
    var match = document.cookie.match(new RegExp('(?:^|; )' + name + '=([^;]*)'));
    return match ? decodeURIComponent(match[1]) : null;
}

function setLoginCookie(value) {
    var oneYear = 60 * 60 * 24 * 365;
    document.cookie = LOGIN_COOKIE + '=' + encodeURIComponent(value) + '; path=/; max-age=' + oneYear + '; samesite=lax';
}

function deleteLoginCookie() {
    document.cookie = LOGIN_COOKIE + '=; path=/; max-age=0; samesite=lax';
}

function showLogin() {
    document.getElementById('loginModal').style.display = 'block';
}

function hideLogin() {
    document.getElementById('loginModal').style.display = 'none';
}

// Robot colors (RobotColor) come from the DB as bare hex ("ff0000"), no leading '#'.
// Pass null/undefined to fall back to the page's default background (GM, or not logged in).
function setPageBackground(hexColor) {
    document.body.style.backgroundColor = hexColor ? ('#' + hexColor) : '';
}

function buildLoginButtons(robots) {
    var html = '';
    for (var i = 0; i < robots.length; i++) {
        var r = robots[i];
        html += "<button class='button' style='margin-bottom:6px; background-color:" + r.RobotColor +
            "; color:" + r.RobotColorFG + ";' onclick='chooseRobotLogin(" + r.RobotID + ");'>" +
            r.RobotName + "</button>";
    }
    document.getElementById('loginRobotButtons').innerHTML = html;
}

function chooseRobotLogin(robotId) {
    setLoginCookie(String(robotId));
    applyLogin();
    showplayerprogram(CurrentLine); // render our own hand immediately, don't wait for the next broadcast
}

function attemptGmLogin() {
    var code = document.getElementById('gmCodeInput').value;
    var errorMsg = document.getElementById('loginError');
    if (code !== GM_CODE) {
        errorMsg.style.display = '';
        return;
    }
    errorMsg.style.display = 'none';
    setLoginCookie(GM_CODE);
    applyLogin();
}

// Reconciles the login cookie against the robots in the *current* game -- called on every
// datapacket update, not just once, since a new game can start with a different roster and
// leave a phone's old cookie pointing at a robot that no longer exists.
function applyLogin() {
    if (!datapacket || !datapacket.robots) return;

    var cookieVal = getCookie(LOGIN_COOKIE);

    if (cookieVal === GM_CODE) {
        IsGM = true;
        LoggedInRobotID = null;
        hideLogin();
        setPageBackground(null); // GM isn't any one robot's color
        return;
    }

    var rbt = cookieVal !== null ? datapacket.robots.find(r => String(r.RobotID) === cookieVal) : null;
    if (rbt) {
        IsGM = false;
        LoggedInRobotID = rbt.RobotID;
        hideLogin();
        setPageBackground(rbt.RobotColor);
        // Just point CurrentLine at our own row -- don't render here. On a fresh page load
        // with an already-matching cookie, this runs on the very first datapacket, before
        // buildPlayerRows() (called from showall(), right after this) has ever cloned the
        // card <img> elements out of <template id="showProgramTemplate">; calling
        // showplayerprogram() this early would hit null elements and throw, aborting the
        // rest of the datapacket listener (including showall() itself). The listener's own
        // trailing showplayerprogram(CurrentLine) call renders once those elements exist.
        CurrentLine = rbt.Priority;
        CurrentPlayer = rbt.RobotID;
        return;
    }

    // No cookie, or it names a robot that isn't part of the current game -- clear out
    // whatever stale value is there (e.g. a robot from a finished game, or the "clearcookies"
    // GM action's empty robots list) and log in again.
    IsGM = false;
    LoggedInRobotID = null;
    setPageBackground(null);
    deleteLoginCookie();
    buildLoginButtons(datapacket.robots);
    showLogin();
}

// ── Direction picker ─────────────────────────────────────────────────────────
// The Direction1.png arrow loops through the 4 facing directions (Up/Right/Down/Left,
// matching MRR.Contracts.Direction's int values 1-4) for whichever robot's hand is currently
// shown. PositionValid is a 3-state flag (DataService.Players.cs has the authoritative
// comment): 0=not set, 1=user set, 2=locked -- GameController.NextState()'s state 4->5 gate
// (AllRobotDirectionsChosen/LockAllRobotDirections) refuses to advance while any robot is
// still 0, then locks every robot to 2 once the turn starts executing, at which point the
// picker disappears everywhere, even in GM mode, until a reboot resets a specific robot's
// PositionValid back to 0 the same way a fresh respawn already does.
//
// For a normal player (and for GM before tapping into GM view, see isGmModeActive below) the
// row is shown only while PositionValid is 0, and "Set Direction" always confirms (sends 1).
// In GM view the row stays visible for 0 or 1 (still hidden at 2, same as everyone), and the
// same button instead toggles between 0 and 1, so GM can flip a robot back to "not yet
// chosen" for testing/admin purposes. Cycling always just writes CurrentPosDir (command 4)
// without touching PositionValid either way.
var DIRECTION_DEGREES = { 1: 0, 2: 90, 3: 180, 4: 270 }; // Up, Right, Down, Left
var pendingDirection = null;         // Direction int (1-4) currently shown on the arrow
var directionPickerForRobotID = null; // which robot pendingDirection belongs to

// GM's extra powers here only kick in once they've tapped into GM view (see "GM view"
// section below) -- logged in as GM but still on the plain player view behaves exactly like
// a player.
function isGmModeActive() {
    return IsGM && gmViewActive;
}

function cycleDirection() {
    pendingDirection = (pendingDirection % 4) + 1; // 1->2->3->4->1
    renderDirectionArrow();
    SendUpdate(4, CurrentPlayer, pendingDirection);
}

function confirmDirection() {
    var rbt = datapacket.robots.find(r => r.RobotID === CurrentPlayer);
    var newValid = (isGmModeActive() && rbt && rbt.PositionValid) ? 0 : 1;
    SendUpdate(5, CurrentPlayer, newValid);
}

// Rotates the on-screen arrow relative to this player's own seat orientation
// (DirectionAdjustment/PlayerViewDirection -- SeatOrientation.Direction, the absolute board
// direction this seat calls "forward") rather than showing the raw board-absolute direction.
// So the arrow reads "pointing up" when the robot faces the same way this seat does,
// whichever physical side of the table the phone is actually on -- the stored value sent to
// the server is still always the real absolute Direction.
function renderDirectionArrow() {
    var rbt = datapacket.robots.find(r => r.RobotID === CurrentPlayer);
    var seatDir = (rbt && rbt.DirectionAdjustment) || 1;
    var degrees = (DIRECTION_DEGREES[pendingDirection] - DIRECTION_DEGREES[seatDir] + 360) % 360;
    document.getElementById('directionBtn').style.transform = 'rotate(' + degrees + 'deg)';
}

function updateDirectionPicker(rbt) {
    var row = document.getElementById('directionPickerRow');
    var gmMode = isGmModeActive();
    // PositionValid: 0=not set, 1=user set, 2=locked (the turn using it already started
    // executing -- GameController.NextState()'s state 4->5 gate). Locked never shows the
    // picker, even for GM; a normal player also never sees it once set (1) or locked (2).
    var hide = (rbt.PositionValid === 2) || (!gmMode && rbt.PositionValid);
    if (hide) {
        row.style.display = 'none';
        pendingDirection = null;
        directionPickerForRobotID = null;
        return;
    }
    row.style.display = '';
    document.getElementById('directionConfirmBtn').textContent =
        (gmMode && rbt.PositionValid) ? 'Clear Direction' : 'Set Direction';
    // Only (re)seed from the server's CurrentPosDir when we start looking at a different
    // robot -- otherwise a broadcast landing between two quick taps would snap the button
    // back to a stale value while the player is still cycling.
    if (directionPickerForRobotID !== rbt.RobotID) {
        directionPickerForRobotID = rbt.RobotID;
        pendingDirection = rbt.Dir || 1;
    }
    renderDirectionArrow();
}

// ── GM view ──────────────────────────────────────────────────────────────────
// GM logs in the same as any player (js/loadrobots.js login section) and by default sees the
// plain player view. Tapping the game-message title flips a GM-only "GM view" that reveals
// extra controls (the Robot-header game-state menu, and Status becoming a per-robot connect
// button) without hiding anything a player already sees.
var gmViewActive = false;
var CONNECT_STATUS_CONNECTED = 22; // tPlayerStatus.RobotConnected (MRR.Contracts/PlayerState.cs)

function toggleGmView() {
    if (!IsGM) return;
    gmViewActive = !gmViewActive;
    if (!gmViewActive) document.getElementById('gmMenu').style.display = 'none';
    showall();
    showplayerprogram(CurrentLine); // refresh the direction picker's GM-mode visibility/label immediately
}

function toggleGmMenu() {
    if (!IsGM || !gmViewActive) return;
    var menu = document.getElementById('gmMenu');
    menu.style.display = (menu.style.display === 'block') ? 'none' : 'block';
}

// Close the GM menu on an outside tap rather than leaving it open over the rest of the page.
document.addEventListener('click', function (ev) {
    var menu = document.getElementById('gmMenu');
    if (menu && menu.style.display === 'block' && !menu.contains(ev.target) && ev.target.id !== 'robotHeader') {
        menu.style.display = 'none';
    }
});

// Start Game / Next State / End Game -- the same /api/state/{action} routes gmindex.html's
// links already use.
function gmAction(action) {
    document.getElementById('gmMenu').style.display = 'none';
    fetch('/api/state/' + action).catch(err => console.error(err.toString()));
}

function toggleRobotConnect(robotId, isConnected) {
    fetch('/api/robot/' + (isConnected ? 'disconnect' : 'connect') + '/' + robotId)
        .catch(err => console.error(err.toString()));
}

// One closure per row's status-cell onclick, so each captures its own robotId/isConnected
// instead of all rows sharing whatever the loop variable last held.
function makeConnectHandler(robotId, isConnected) {
    return function () { toggleRobotConnect(robotId, isConnected); };
}

function buildPlayerRows(robots) {
    if (robots.length === lastPlayerCount) return;
    lastPlayerCount = robots.length;

    var showProgramContent = document.getElementById('showProgramTemplate').innerHTML;
    var html = '';
    for (var i = 0; i < robots.length; i++) {
        var rid = i + 1;
        var showProgramTd = i === 0
            ? "<td align=center rowspan='" + robots.length + "' bgcolor='#e0e0e0' id='showprogram'>" + showProgramContent + "</td>"
            : '';
        html += "<tr id='tr" + rid + "'>" +
            "<td align=center><button class='button' id='button" + rid + "' style='background-color:e0e0e0; color:000000' onclick='showplayerprogram(" + rid + ");'>--</button></td>" +
            "<td align=center id='flags" + rid + "' bgcolor='#e0e0e0'>--</td>" +
            "<td align=center id='playerstatus" + rid + "'>--</td>" +
            showProgramTd +
            "</tr>";
    }
    document.getElementById('playerRows').innerHTML = html;
}

function showplayerprogram(pl) // show program for this line
{
    robots = datapacket.robots;
    var rbt = robots.find(r => r.Priority === pl);
    if (!rbt) return;

    // Closed system, but still only show a phone its own hand: GM can look at any row,
    // a logged-in player cannot switch away from theirs.
    if (!IsGM && LoggedInRobotID !== null && rbt.RobotID !== LoggedInRobotID) return;

    CurrentLine = pl;
    CurrentPlayer = rbt.RobotID;
    var dealt = rbt.CardsDealt.split(",");
    var played = rbt.CardsPlayed.split(",");
    var executed = rbt.StatusToShow.split(",");
//    var messagetype = rbt.msgtype;
    var message = rbt.PlayerMsg;

    //console.log("Robot:" ,CurrentLine, " line: ", pl );

    for(var i=0;i<5;i++)
    {
//            console.log("played:" ,i,played[i] );
        var card = document.getElementById("Played" + i);
        //var cardimg = "images/Blank.png";
        var cardimg = "images/type0.png";
        var cardtag = 0;
        if (i<played.length && played[i]!="") 
        {
            cardimg = "images/type" + played[i] + ".png";
            cardtag = played[i];
            //console.log(executed);
            //if (executed[i].length == 1 && executed[i] != "X" && executed[i] != null)
            //{
                //document.getElementById("CardCell" + i).style.backgroundColor = "ccccff";
            //}
        }
        card.src = cardimg;
        card.tag = cardtag;
        card.loc = i+1;
        card.cid = -1;
    }

    for(var i=0;i<10;i++)
    {
        var card = document.getElementById("Dealt" + i);
        //var cardimg = "images/Blank.png";
        var cardimg = "images/type0.png";
        var cardtag = 0;
        if (i<dealt.length && dealt[i]!="") 
        {
            cardimg = "images/type" + dealt[i] + ".png";
            cardtag = dealt[i];
        }
        card.src = cardimg;
        card.tag = cardtag;
        card.loc = -1;
        card.cid = Number(cardtag);
    }

    var active = canProgram();
    var displayVal = active ? '' : 'none';
    document.getElementById("DealtRow1").style.display = displayVal;
    document.getElementById("DealtRow2").style.display = displayVal;

    var showmessage = "display: none;";
    //console.log("Message:", message);

    if (message!="" && message != "undefined")
    {
        showmessage = "";
        document.getElementById("btnMessagebox").textContent = message;
    }
    document.getElementById("messagetable").style = showmessage;
    document.getElementById("messagetablespace").style = showmessage;

    updateDirectionPicker(rbt);
}

function showall()
{
    robots = datapacket.robots
    buildPlayerRows(robots);

    //robotjson = robots;
    document.getElementById("title").innerText = datapacket.titlemsg;

    var showGmControls = IsGM && gmViewActive;
    document.getElementById('robotHeader').style.cursor = showGmControls ? 'pointer' : 'default';
    document.getElementById('statusHeader').innerText = showGmControls ? 'Connect' : 'Status';
    if (!showGmControls) document.getElementById('gmMenu').style.display = 'none';

    for(var i = 0;i<robots.length;i++)
    {
        //console.log(robots[i]);
        var rid = robots[i].Priority;
        var btn = document.getElementById("button" + rid);
        btn.style = "background-color:" + robots[i].RobotColor + "; color:" + robots[i].RobotColorFG;
        btn.textContent = robots[i].RobotName;
        document.getElementById("flags" + rid).innerText = robots[i].FlagEnergyCards;

        var statusbox = document.getElementById("playerstatus" + rid);
        if (showGmControls) {
            // Robot Connection Screen's info, not gameplay status -- red/yellow/green/purple
            // per RobotStatus already encodes "not connected" as red, so no need to special-
            // case that color here (see install/todo.md Section 8).
            statusbox.innerText = robots[i].ConnectStatusDesc;
            statusbox.style.backgroundColor = robots[i].ConnectStatusColor;
            statusbox.style.cursor = 'pointer';
            statusbox.onclick = makeConnectHandler(robots[i].RobotID, robots[i].ConnectStatusID === CONNECT_STATUS_CONNECTED);
        } else {
            statusbox.innerText = robots[i].StatusToShow;
            statusbox.style.backgroundColor = robots[i].StatusColor;
            statusbox.style.cursor = 'default';
            statusbox.onclick = null;
        }

        document.getElementById("tr" + rid).style = "";
    }

}

function canProgram()
{
    var gs = datapacket && datapacket.gamestate;
    return gs >= 2 && gs <= 4;
}

function PlayCard(cardObj)
{
    if (!canProgram()) return;
    SendUpdate( 1, CurrentPlayer, cardObj.cid, cardObj.loc);
}

function confirmMessage()
{
    SendUpdate( 3, CurrentPlayer);
}

function SendUpdate(command, playerid=0, data1=0, data2=0)
{
    //console.log("SendUpdate:", command, playerid, data1, data2);
    fetch(`/api/player/${command}/${playerid}/${data1}/${data2}`)
        .catch(err => console.error(err.toString()));
}

// connection/reconnect handling lives in js/datahub-connection.js; this listens for
// the payload it dispatches.
document.addEventListener('datapacket', function (ev) {
    datapacket = ev.detail;
    applyLogin();
    showall();
    showplayerprogram(CurrentLine);
});

