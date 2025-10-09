
var CurrentPlayer = 0;
var CurrentLine = 0;
var datapacket = null;

function showplayerprogram(pl) // show program for this line
{
    robots = datapacket.robots;
    CurrentLine = pl;
    if (pl<1 || pl>robots.length) return;
    var rbt = robots[pl-1];
    CurrentPlayer = rbt.RobotID;
    var dealt = rbt.CardsDealt.split(",");
    var played = rbt.CardsPlayed.split(",");
    var executed = rbt.StatusToShow.split(",");
//    var messagetype = rbt.msgtype;
    var message = rbt.msg;

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
            if (executed.length == 1 && executed[i] != "X" && executed[i] != null)
            {
                //document.getElementById("CardCell" + i).style.backgroundColor = "ccccff";
            }
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

    var showmessage = "display: none;";

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

    //robotjson = robots;
    for(var i = 0;i<robots.length;i++)
    {
        //console.log(robots[i]);
        var rid = robots[i].Priority;
        var btn = document.getElementById("button" + rid);
        btn.style = "background-color:" + robots[i].RobotColor + "; color:" + robots[i].RobotColorFG;
        btn.textContent = robots[i].RobotName;
        //document.getElementById("flags" + rid).innerText = robots[i].FlagEnergy;
        for(var j=0;j<5;j++)
        {
            var color1 = "ffffff";
            if(j<robots[i].CurrentFlag) color1 = "0000ff";
            document.getElementById("flag" + rid + j).style.backgroundColor = color1;
            
        }
        for(var j=0;j<5;j++)
        {
            var color1 = "ffffff";
            if(j<robots[i].Energy) color1 = "00ff00";
            document.getElementById("energy" + rid + j).style.backgroundColor = color1;
            
        }
        //$("#flags" + rid).innerText = robots[i].FlagEnergy;
        var statusbox = document.getElementById("playerstatus" + rid);
        statusbox.innerText = robots[i].StatusToShow;
        statusbox.style.backgroundColor = robots[i].StatusColor;
    }

}

function PlayCard(cardObj)
{
    SendUpdate( 1, CurrentPlayer, cardObj.cid, cardObj.loc);
}

function confirmMessage()
{
    SendUpdate( 3, CurrentPlayer);
}

function SendUpdate( command,  playerid=0,  data1=0,  data2=0)
{
    connection.invoke("UpdatePlayer", command, playerid, data1, data2)
        .catch(err => console.error(err.toString()));
}

// signalR part
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/datahub") 
    .build();

connection.on("AllDataUpdate", function (data) {
    console.log("Data received from server:", data);

    // The server may now send a JSON string (or an object). Normalize to an object.
    if (typeof data === 'string') {
        try {
            data = JSON.parse(data);
        } catch (err) {
            console.error('Failed to parse AllDataUpdate payload as JSON', err, data);
            return;
        }
    }

    datapacket = data;
    showall();
    showplayerprogram(CurrentLine);
});

connection.start().then(() => {
    console.log("SignalR Connected!");
}).catch(function (err) {
    console.error(err.toString());
});

