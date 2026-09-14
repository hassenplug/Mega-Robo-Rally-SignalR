
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

    // No cookie, or it names a robot that isn't part of the current game -- log in again.
    IsGM = false;
    LoggedInRobotID = null;
    setPageBackground(null);
    buildLoginButtons(datapacket.robots);
    showLogin();
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

}

function showall()
{
    robots = datapacket.robots
    buildPlayerRows(robots);

    //robotjson = robots;
    document.getElementById("title").innerText = datapacket.titlemsg;
    for(var i = 0;i<robots.length;i++)
    {
        //console.log(robots[i]);
        var rid = robots[i].Priority;
        var btn = document.getElementById("button" + rid);
        btn.style = "background-color:" + robots[i].RobotColor + "; color:" + robots[i].RobotColorFG;
        btn.textContent = robots[i].RobotName;
        document.getElementById("flags" + rid).innerText = robots[i].FlagEnergyCards;
        var statusbox = document.getElementById("playerstatus" + rid);
        statusbox.innerText = robots[i].StatusToShow;
        statusbox.style.backgroundColor = robots[i].StatusColor;
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

