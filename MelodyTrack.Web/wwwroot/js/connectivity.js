let dotNetObject = null;

export function setupConnectivityListeners(dotNetRef) {
    dotNetObject = dotNetRef;

    const updateStatus = () => {
        dotNetObject.invokeMethodAsync("OnConnectivityChanged", navigator.onLine);
    };

    window.addEventListener("online", updateStatus);
    window.addEventListener("offline", updateStatus);

    // Notify initial state
    updateStatus();
}

export function isOnline() {
    return navigator.onLine;
}

export function cleanupConnectivityListeners() {
    window.removeEventListener("online", updateStatus);
    window.removeEventListener("offline", updateStatus);
}
