// Shared SignalR connection to /datahub used by index.html, connectscreen.html and
// board-viewer.html. Handles connect/reconnect and the initial /api/alldata fetch;
// each page listens for the "datapacket" event on `document` to get the payload.
(function () {
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
        document.dispatchEvent(new CustomEvent('datapacket', { detail: data }));
    });

    connection.onreconnecting(function (error) {
        console.warn('SignalR connection lost. Reconnecting...', error);
    });

    connection.onreconnected(function (connectionId) {
        console.log('SignalR reconnected. ConnectionId:', connectionId);
    });

    connection.onclose(function (error) {
        console.error('SignalR connection closed.', error);
        setTimeout(startConnection, 2000);
    });

    function startConnection() {
        connection.start().then(function () {
            console.log("SignalR Connected!");
            fetch('/api/alldata').catch(function (err) { console.error('Initial data fetch failed', err); });
        }).catch(function (err) {
            console.error('SignalR failed to connect, retrying in 2s', err.toString());
            setTimeout(startConnection, 2000);
        });
    }

    window.datahubConnection = connection;
    startConnection();
})();
