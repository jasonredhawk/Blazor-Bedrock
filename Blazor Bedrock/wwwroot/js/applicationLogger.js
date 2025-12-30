window.applicationLogger = {
    init: function (dotNetRef) {
        // Keyboard shortcut: CTRL+`
        document.addEventListener('keydown', function (e) {
            if (e.ctrlKey && e.key === '`') {
                e.preventDefault();
                dotNetRef.invokeMethodAsync('ShowLogger');
            }
        });

        // Intercept console methods
        const originalLog = console.log;
        const originalError = console.error;
        const originalWarn = console.warn;
        const originalInfo = console.info;
        const originalDebug = console.debug;

        console.log = function (...args) {
            originalLog.apply(console, args);
            // Could send to Blazor if needed
        };

        console.error = function (...args) {
            originalError.apply(console, args);
            // Could send to Blazor if needed
        };

        console.warn = function (...args) {
            originalWarn.apply(console, args);
            // Could send to Blazor if needed
        };

        console.info = function (...args) {
            originalInfo.apply(console, args);
            // Could send to Blazor if needed
        };

        console.debug = function (...args) {
            originalDebug.apply(console, args);
            // Could send to Blazor if needed
        };

        // Global error handler
        window.addEventListener('error', function (e) {
            // Could send to Blazor if needed
        });

        window.addEventListener('unhandledrejection', function (e) {
            // Could send to Blazor if needed
        });
    }
};

// Simple Enter key handler for chat
window.setupChatEnterKey = function (dotNetRef) {
    // Remove any existing interval
    if (window.chatEnterKeyInterval) {
        clearInterval(window.chatEnterKeyInterval);
    }
    
    // Simple function to attach handler
    function attachHandler() {
        const container = document.getElementById('chat-input-container');
        if (!container) return;
        
        const textarea = container.querySelector('textarea');
        if (!textarea) return;
        
        // Remove old handler if exists
        if (textarea.dataset.handlerAttached === 'true') {
            textarea.removeEventListener('keydown', textarea._enterKeyHandler);
        }
        
        // Create new handler
        textarea._enterKeyHandler = function(e) {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                e.stopPropagation();
                e.stopImmediatePropagation();
                dotNetRef.invokeMethodAsync('HandleEnterKey').catch(function(err) {
                    console.error('Error calling HandleEnterKey:', err);
                });
                return false;
            }
        };
        
        textarea.addEventListener('keydown', textarea._enterKeyHandler, true);
        textarea.dataset.handlerAttached = 'true';
    }
    
    // Try immediately and on interval
    attachHandler();
    window.chatEnterKeyInterval = setInterval(attachHandler, 300);
};

