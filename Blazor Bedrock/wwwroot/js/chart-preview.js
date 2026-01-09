// Chart Preview Helper for Blazor
window.chartPreview = {
    chartInstances: {},

    // Render or update chart preview
    renderChart: function (canvasId, chartData) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) {
            console.error('Canvas element not found:', canvasId);
            return;
        }

        const ctx = canvas.getContext('2d');

        // Destroy existing chart if it exists for this canvas
        if (this.chartInstances[canvasId]) {
            this.chartInstances[canvasId].destroy();
            delete this.chartInstances[canvasId];
        }

        // Parse chart data
        const config = JSON.parse(chartData);

        // Create new chart and store it
        this.chartInstances[canvasId] = new Chart(ctx, config);

        return true;
    },

    // Destroy chart instance for a specific canvas
    destroyChart: function (canvasId) {
        if (this.chartInstances[canvasId]) {
            this.chartInstances[canvasId].destroy();
            delete this.chartInstances[canvasId];
        }
    },

    // Destroy all chart instances
    destroyAllCharts: function () {
        for (const canvasId in this.chartInstances) {
            if (this.chartInstances.hasOwnProperty(canvasId)) {
                this.chartInstances[canvasId].destroy();
            }
        }
        this.chartInstances = {};
    }
};
