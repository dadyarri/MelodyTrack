export function isFileSystemApiSupported() {
    return 'showOpenFilePicker' in window || 'showSaveFilePicker' in window || 'FileSystemHandle' in window;
}

let directoryHandle = null;

export async function hasValidSavedDirectory() {
    const db = await getDatabase();
    const tx = db.transaction("directory", "readonly");
    const store = tx.objectStore("directory");
    directoryHandle = await store.get("current");

    if (!directoryHandle) {
        return null; // No saved directory handle
    }

    // Check if the handle is still valid and permissions are granted
    const permission = await directoryHandle.queryPermission({mode: "readwrite"});
    if (permission === "granted") {
        return directoryHandle; // Handle is valid
    } else {
        return null; // Handle is invalid or permissions are missing
    }
}

// Store directory handle persistently using IndexedDB (since it supports storing handles)
export async function selectDirectory() {
    directoryHandle = hasValidSavedDirectory();
    if (directoryHandle === null) {
        directoryHandle = await window.showDirectoryPicker();
        await persistDirectoryHandle(directoryHandle);
        return !!directoryHandle;
    }
    
    return true;

}

// Persist the directory handle
async function persistDirectoryHandle(handle) {
    const db = await getDatabase();
    const tx = db.transaction("directory", "readwrite");
    const store = tx.objectStore("directory");
    await store.put(handle, "current");
    await tx.done;
}

// Restore the directory handle
export async function restoreDirectory() {
    const db = await getDatabase();
    const tx = db.transaction("directory", "readonly");
    const store = tx.objectStore("directory");
    directoryHandle = await store.get("current");
    return !!directoryHandle; // Return true if the handle was successfully restored
}

// Read a file; return null if the file doesn't exist
export async function readFile(fileName) {
    if (!directoryHandle) {
        throw new Error("Directory handle not set. Please select a directory.");
    }

    try {
        const fileHandle = await directoryHandle.getFileHandle(fileName, {create: false});
        const file = await fileHandle.getFile();
        return await file.json();
    } catch (error) {
        if (error.name === "NotFoundError") {
            return []; // File does not exist
        }
        throw error; // Other errors should propagate
    }
}

// Write to a file; create it if it doesn't exist
export async function writeFile(fileName, data) {
    if (!directoryHandle) {
        throw new Error("Directory handle not set. Please select a directory.");
    }

    const fileHandle = await directoryHandle.getFileHandle(fileName, {create: true});
    const writable = await fileHandle.createWritable();
    await writable.write(data);
    await writable.close();
}

// Utility: Initialize IndexedDB
async function getDatabase() {
    const db = await indexedDB.open("MelodyTrackOptions", 1, {
        upgrade(db) {
            if (!db.objectStoreNames.contains("directory")) {
                db.createObjectStore("directory");
            }
        },
    });
    return db;
}

