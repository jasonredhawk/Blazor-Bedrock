// Resizable Splitter for dividing panels
window.resizableSplitter = {
    init: function(leftPanelId, rightPanelId, splitterId, initialLeftWidth = 30) {
        const leftPanel = document.getElementById(leftPanelId);
        const rightPanel = document.getElementById(rightPanelId);
        const splitter = document.getElementById(splitterId);
        
        if (!leftPanel || !rightPanel || !splitter) {
            console.error('Resizable splitter: Required elements not found');
            return;
        }
        
        const container = leftPanel.parentElement;
        if (!container) return;
        
        let isResizing = false;
        let startX = 0;
        let startLeftWidth = 0;
        
        // Set initial widths
        const containerWidth = container.clientWidth;
        const leftWidthPercent = initialLeftWidth;
        const rightWidthPercent = 100 - leftWidthPercent;
        
        leftPanel.style.width = `${leftWidthPercent}%`;
        rightPanel.style.width = `${rightWidthPercent}%`;
        leftPanel.style.flexShrink = '0';
        rightPanel.style.flexShrink = '1';
        rightPanel.style.flexGrow = '1';
        
        // Mouse down on splitter
        splitter.addEventListener('mousedown', (e) => {
            isResizing = true;
            startX = e.clientX;
            startLeftWidth = leftPanel.offsetWidth;
            document.body.style.cursor = 'col-resize';
            document.body.style.userSelect = 'none';
            e.preventDefault();
        });
        
        // Mouse move
        document.addEventListener('mousemove', (e) => {
            if (!isResizing) return;
            
            const deltaX = e.clientX - startX;
            const containerWidth = container.clientWidth;
            const newLeftWidth = startLeftWidth + deltaX;
            const minLeftWidth = 200; // Minimum width for left panel
            const maxLeftWidth = containerWidth - 400; // Minimum width for right panel
            
            if (newLeftWidth >= minLeftWidth && newLeftWidth <= maxLeftWidth) {
                const leftPercent = (newLeftWidth / containerWidth) * 100;
                const rightPercent = 100 - leftPercent;
                
                leftPanel.style.width = `${leftPercent}%`;
                rightPanel.style.width = `${rightPercent}%`;
            }
        });
        
        // Mouse up
        document.addEventListener('mouseup', () => {
            if (isResizing) {
                isResizing = false;
                document.body.style.cursor = '';
                document.body.style.userSelect = '';
            }
        });
        
        // Touch support for mobile
        splitter.addEventListener('touchstart', (e) => {
            isResizing = true;
            startX = e.touches[0].clientX;
            startLeftWidth = leftPanel.offsetWidth;
            e.preventDefault();
        });
        
        document.addEventListener('touchmove', (e) => {
            if (!isResizing) return;
            const deltaX = e.touches[0].clientX - startX;
            const containerWidth = container.clientWidth;
            const newLeftWidth = startLeftWidth + deltaX;
            const minLeftWidth = 200;
            const maxLeftWidth = containerWidth - 400;
            
            if (newLeftWidth >= minLeftWidth && newLeftWidth <= maxLeftWidth) {
                const leftPercent = (newLeftWidth / containerWidth) * 100;
                const rightPercent = 100 - leftPercent;
                leftPanel.style.width = `${leftPercent}%`;
                rightPanel.style.width = `${rightPercent}%`;
            }
            e.preventDefault();
        });
        
        document.addEventListener('touchend', () => {
            isResizing = false;
        });
        
        console.log('Resizable splitter initialized');
    }
};
