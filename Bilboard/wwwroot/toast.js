function showToast() {
    // Get the toast element directly
    var toastLiveExample = document.getElementById('liveToast');

    // Access bootstrap from the window object
    if (toastLiveExample && typeof window.bootstrap !== 'undefined') {
        try {
            var toast = new window.bootstrap.Toast(toastLiveExample);
            toast.show();
            console.log("Toast shown successfully");
        } catch (error) {
            console.error("Error showing toast:", error);
        }
    } else {
        console.error("Bootstrap not available or toast element not found");
    }
}