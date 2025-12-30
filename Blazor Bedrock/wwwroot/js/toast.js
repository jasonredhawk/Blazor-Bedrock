// Bootstrap Toast Notification Helper
window.showToast = (type, message) => {
    // Create toast container if it doesn't exist
    let toastContainer = document.getElementById('toast-container');
    if (!toastContainer) {
        toastContainer = document.createElement('div');
        toastContainer.id = 'toast-container';
        toastContainer.className = 'toast-container position-fixed top-0 end-0 p-3';
        toastContainer.style.zIndex = '9999';
        document.body.appendChild(toastContainer);
    }

    // Create toast element
    const toastId = 'toast-' + Date.now();
    const toast = document.createElement('div');
    toast.id = toastId;
    toast.className = 'toast';
    toast.setAttribute('role', 'alert');
    toast.setAttribute('aria-live', 'assertive');
    toast.setAttribute('aria-atomic', 'true');

    // Determine toast color based on type
    let bgClass = 'bg-primary';
    let icon = '';
    if (type === 'success') {
        bgClass = 'bg-success';
        icon = '<i class="bi bi-check-circle-fill me-2"></i>';
    } else if (type === 'error') {
        bgClass = 'bg-danger';
        icon = '<i class="bi bi-x-circle-fill me-2"></i>';
    } else if (type === 'warning') {
        bgClass = 'bg-warning';
        icon = '<i class="bi bi-exclamation-triangle-fill me-2"></i>';
    } else if (type === 'info') {
        bgClass = 'bg-info';
        icon = '<i class="bi bi-info-circle-fill me-2"></i>';
    }

    toast.innerHTML = `
        <div class="toast-header ${bgClass} text-white">
            ${icon}
            <strong class="me-auto">Notification</strong>
            <button type="button" class="btn-close btn-close-white" data-bs-dismiss="toast" aria-label="Close"></button>
        </div>
        <div class="toast-body">
            ${message}
        </div>
    `;

    toastContainer.appendChild(toast);

    // Initialize and show toast
    const bsToast = new bootstrap.Toast(toast, {
        autohide: true,
        delay: 5000
    });
    bsToast.show();

    // Remove toast element after it's hidden
    toast.addEventListener('hidden.bs.toast', () => {
        toast.remove();
    });
};
