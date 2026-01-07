// Keyword Network Visualization using D3.js
// Blueprint-style layout with Q&A clusters

window.keywordNetworkVisualization = {
    // Configuration
    config: {
        nodeSizeRange: [20, 50],
        linkWidthRange: [0.5, 4],
        colors: {
            userRequest: '#0d6efd', // Blue
            aiResponse: '#198754', // Green
            concept: '#6f42c1', // Purple
            manualKeyword: '#ff6b35', // Orange
            link: '#6c757d',
            accent: '#ffc107',
            background: '#fafafa',
            grid: '#e0e0e0',
            text: '#212529',
            blueprint: '#2c3e50'
        },
        animationDuration: 750,
        clusterSpacing: { x: 500, y: 500 }, // Space between Q&A cluster centers
        nodeSpacing: { x: 100, y: 80 }, // Space between nodes in cluster (not used in spiral)
        padding: { top: 80, right: 80, bottom: 80, left: 80 },
        spiralRadius: 100, // Base radius for spiral nodes
        manualClusterOffset: { x: 0, y: 0 } // Will be calculated
    },

    // State
    svg: null,
    nodes: [],
    links: [],
    allMessages: [],
    manualKeywords: [],
    extractedData: null,
    clusters: [], // Q&A clusters
    manualCluster: null, // Manual keywords cluster

    // Initialize the visualization
    init: function(containerId, dotNetRef) {
        const container = document.getElementById(containerId);
        if (!container) {
            console.error(`Container with id "${containerId}" not found. Retrying in 200ms...`);
            setTimeout(() => this.init(containerId, dotNetRef), 200);
            return;
        }

        if (container.clientWidth === 0 || container.clientHeight === 0) {
            console.warn(`Container "${containerId}" has zero dimensions. Retrying in 200ms...`);
            setTimeout(() => this.init(containerId, dotNetRef), 200);
            return;
        }

        console.log(`Initializing keyword network visualization in container: ${containerId}`);

        // Clear any existing visualization
        d3.select(`#${containerId} svg`).remove();

        const width = container.clientWidth || 800;
        const height = container.clientHeight || 600;

        // Create SVG
        this.svg = d3.select(`#${containerId}`)
            .append('svg')
            .attr('width', width)
            .attr('height', height)
            .attr('viewBox', [0, 0, width, height])
            .attr('preserveAspectRatio', 'xMidYMid meet')
            .style('background', this.config.colors.background);

        // Add zoom behavior
        const zoom = d3.zoom()
            .scaleExtent([0.1, 3])
            .on('zoom', (event) => {
                this.svg.select('g.container').attr('transform', event.transform);
            });

        this.svg.call(zoom);

        // Create container group for zoom/pan
        const containerGroup = this.svg.append('g')
            .attr('class', 'container');

        // Create groups for grid, links, and nodes
        containerGroup.append('g').attr('class', 'grid');
        containerGroup.append('g').attr('class', 'links');
        containerGroup.append('g').attr('class', 'nodes');
        containerGroup.append('g').attr('class', 'labels');

        this.containerId = containerId;
        this.dotNetRef = dotNetRef;

        window.addEventListener('resize', () => this.handleResize());
        console.log('Keyword network visualization initialized');
    },

    // Handle window resize
    handleResize: function() {
        if (!this.svg) return;
        const container = document.getElementById(this.containerId);
        if (!container) return;
        const width = container.clientWidth || 800;
        const height = container.clientHeight || 600;
        this.svg
            .attr('width', width)
            .attr('height', height)
            .attr('viewBox', [0, 0, width, height]);
    },

    // Normalize messages from Blazor
    normalizeMessages: function(messages) {
        if (!messages) return [];
        if (Array.isArray(messages)) return messages;
        if (messages.length !== undefined) return Array.from(messages);
        return [messages];
    },

    // Calculate iterations from actual message sequence - CRITICAL: Every user question = new iteration
    calculateIterationsFromMessages: function(messages) {
        const iterations = [];
        let currentIteration = -1; // Start at -1, increment when we see a user message
        let lastRole = null;

        console.log(`\n=== CALCULATING ITERATIONS FROM ${messages.length} MESSAGES ===`);

        messages.forEach((message, index) => {
            const role = (message.role || message.Role || '').toLowerCase();
            const content = message.content || message.Content || '';
            
            if (role === 'user') {
                // EVERY user message starts a new iteration (or continues current if no AI response yet)
                // If we already have a user message in current iteration without an AI response, keep same iteration
                // Otherwise, start new iteration
                const lastIter = iterations[iterations.length - 1];
                if (lastIter && lastIter.role === 'user' && lastIter.iterationIndex === currentIteration) {
                    // Previous was user, keep same iteration (multiple user messages before AI response)
                    // Actually, let's make each user message its own iteration
                    currentIteration++;
                } else if (currentIteration === -1 || lastRole === 'assistant') {
                    // First message or after assistant = new iteration
                    currentIteration++;
                }
                
                iterations.push({
                    messageIndex: index,
                    iterationIndex: currentIteration,
                    role: 'user',
                    content: content
                });
                console.log(`  Message ${index}: USER → Iteration ${currentIteration} (${content.substring(0, 50)}...)`);
            } else if (role === 'assistant') {
                // Assistant message belongs to current iteration (the one with the user question)
                if (currentIteration < 0) {
                    // If no user message yet, create iteration 0
                    currentIteration = 0;
                }
                iterations.push({
                    messageIndex: index,
                    iterationIndex: currentIteration,
                    role: 'assistant',
                    content: content
                });
                console.log(`  Message ${index}: ASSISTANT → Iteration ${currentIteration} (${content.substring(0, 50)}...)`);
                // Next user message will start new iteration
            } else if (role === 'system') {
                // System messages don't count as iterations
                console.log(`  Message ${index}: SYSTEM (ignored)`);
            }
            
            lastRole = role;
        });

        const uniqueIterations = new Set(iterations.filter(i => i.role === 'user').map(i => i.iterationIndex));
        console.log(`\n✓ Calculated ${uniqueIterations.size} unique iterations (${iterations.length} total message entries)`);
        console.log(`  Iteration indices:`, Array.from(uniqueIterations).sort((a, b) => a - b));
        
        return iterations;
    },

    // Find best iteration match for a node by content similarity
    findBestIterationMatch: function(node, messageIterations, nodeType) {
        const nodeText = (node.label || node.Label || node.originalText || node.OriginalText || '').toLowerCase().trim();
        if (!nodeText || nodeText.length < 5) return -1;

        let bestMatch = -1;
        let bestScore = 0;

        // Filter iterations by role
        const relevantIterations = messageIterations.filter(i => i.role === nodeType);
        
        relevantIterations.forEach(iter => {
            const msgText = iter.content.toLowerCase().trim();
            
            // Calculate similarity score
            let score = 0;
            
            // Exact substring match (high score)
            if (nodeText.includes(msgText.substring(0, Math.min(50, msgText.length))) || 
                msgText.includes(nodeText.substring(0, Math.min(50, nodeText.length)))) {
                score += 10;
            }
            
            // Word overlap
            const nodeWords = nodeText.split(/\s+/).filter(w => w.length > 3);
            const msgWords = msgText.split(/\s+/).filter(w => w.length > 3);
            const commonWords = nodeWords.filter(w => msgWords.includes(w));
            score += commonWords.length * 2;
            
            // Length similarity
            const lengthDiff = Math.abs(nodeText.length - msgText.length);
            const maxLength = Math.max(nodeText.length, msgText.length);
            if (maxLength > 0) {
                score += (1 - lengthDiff / maxLength) * 3;
            }
            
            if (score > bestScore) {
                bestScore = score;
                bestMatch = iter.iterationIndex;
            }
        });

        if (bestScore > 5) { // Threshold for a good match
            console.log(`  Matched "${nodeText.substring(0, 40)}" to iteration ${bestMatch} (score: ${bestScore.toFixed(1)})`);
            return bestMatch;
        }

        return -1;
    },

    // Create fallback nodes from messages if ChatGPT extraction fails
    // CRITICAL: This MUST create nodes for ALL questions in the conversation
    createFallbackNodes: function() {
        console.log(`\n=== CREATING FALLBACK NODES FROM ${this.allMessages.length} MESSAGES ===`);
        this.extractedData = {
            userRequestNodes: [],
            aiResponseNodes: [],
            conceptNodes: []
        };

        let iterationIndex = 0;
        let currentUserMessage = null;
        let currentUserIteration = -1;
        let lastRole = null;

        this.allMessages.forEach((message, index) => {
            const role = (message.role || message.Role || '').toLowerCase();
            const content = message.content || message.Content || '';

            // Track Q&A pairs: EVERY user message = new iteration
            if (role === 'user') {
                // If previous was also user OR we're starting fresh OR after an assistant = new iteration
                if (lastRole === 'user' || lastRole === 'assistant' || lastRole === null) {
                    iterationIndex++;
                    currentUserIteration = iterationIndex - 1; // 0-indexed
                } else {
                    // Continue current iteration
                    currentUserIteration = iterationIndex - 1;
                }
                
                currentUserMessage = content;
                
                // Create user request node for THIS iteration
                this.extractedData.userRequestNodes.push({
                    id: `user_${currentUserIteration}`,
                    label: content.substring(0, 60) + (content.length > 60 ? '...' : ''),
                    type: 'userRequest',
                    relevance: 0.7,
                    iterationIndex: currentUserIteration,
                    originalText: content
                });
                console.log(`✓ Created user request node for iteration ${currentUserIteration}: "${content.substring(0, 50)}..."`);
            } 
            else if (role === 'assistant') {
                // Find the most recent user message iteration
                if (currentUserIteration >= 0) {
                    // Create AI response node for current iteration
                    this.extractedData.aiResponseNodes.push({
                        id: `ai_${currentUserIteration}`,
                        label: content.substring(0, 60) + (content.length > 60 ? '...' : ''),
                        type: 'aiResponse',
                        relevance: 0.7,
                        iterationIndex: currentUserIteration,
                        originalText: content
                    });
                    console.log(`✓ Created AI response node for iteration ${currentUserIteration}: "${content.substring(0, 50)}..."`);

                    // Extract simple keywords from AI response as concepts
                    const words = content.toLowerCase()
                        .replace(/[^\w\s]/g, ' ')
                        .split(/\s+/)
                        .filter(w => w.length > 4)
                        .slice(0, 5); // Top 5 words

                    words.forEach((word, wIndex) => {
                        this.extractedData.conceptNodes.push({
                            id: `concept_${currentUserIteration}_${word}`,
                            label: word,
                            type: 'concept',
                            relevance: 0.5 + (0.3 * (1 - wIndex / words.length)),
                            iterationIndex: currentUserIteration,
                            originalText: word
                        });
                    });
                } else {
                    // No user message yet, create iteration 0
                    currentUserIteration = 0;
                    this.extractedData.aiResponseNodes.push({
                        id: `ai_0`,
                        label: content.substring(0, 60) + (content.length > 60 ? '...' : ''),
                        type: 'aiResponse',
                        relevance: 0.7,
                        iterationIndex: 0,
                        originalText: content
                    });
                }
            }
            
            lastRole = role;
        });

        const uniqueIterations = new Set(this.extractedData.userRequestNodes.map(n => n.iterationIndex));
        console.log(`\n✓ Created fallback nodes:`);
        console.log(`  - ${this.extractedData.userRequestNodes.length} user requests`);
        console.log(`  - ${this.extractedData.aiResponseNodes.length} AI responses`);
        console.log(`  - ${this.extractedData.conceptNodes.length} concepts`);
        console.log(`  - ${uniqueIterations.size} unique iterations:`, Array.from(uniqueIterations).sort((a, b) => a - b));
    },

    // Extract concepts using ChatGPT API
    extractConcepts: async function(messages) {
        const normalizedMessages = this.normalizeMessages(messages);
        
        const requestBody = {
            messages: normalizedMessages.map(msg => ({
                role: msg.role || msg.Role || '',
                content: msg.content || msg.Content || ''
            }))
        };

        try {
            const response = await fetch('/api/conversations/extract-concepts', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                credentials: 'include',
                body: JSON.stringify(requestBody)
            });

            if (!response.ok) {
                throw new Error(`HTTP error! status: ${response.status}`);
            }

            const result = await response.json();
            console.log('ChatGPT extraction result:', result);
            return result;
        } catch (error) {
            console.error('Error extracting concepts:', error);
            return null;
        }
    },

    // Add manual keywords
    addManualKeywords: function(keywordsString) {
        if (!keywordsString || typeof keywordsString !== 'string') return;
        const keywords = keywordsString.split(',').map(k => k.trim()).filter(k => k.length > 0);
        keywords.forEach(keyword => {
            if (!this.manualKeywords.includes(keyword)) {
                this.manualKeywords.push(keyword);
            }
        });
        if (this.nodes.length > 0) {
            this.buildGraph();
            this.render();
        }
    },

    // Remove manual keyword
    removeManualKeyword: function(keyword) {
        this.manualKeywords = this.manualKeywords.filter(k => k !== keyword);
        if (this.nodes.length > 0) {
            this.buildGraph();
            this.render();
        }
    },

    // Clear manual keywords
    clearManualKeywords: function() {
        this.manualKeywords = [];
        if (this.nodes.length > 0) {
            this.buildGraph();
            this.render();
        }
    },

    // Update visualization with new messages
    update: async function(messages) {
        if (!this.svg) {
            console.warn('Visualization not initialized. Call init() first.');
            return;
        }

        const normalizedMessages = this.normalizeMessages(messages || []);
        
        // Count questions before updating
        const questionCount = this.countQuestions(normalizedMessages);
        console.log(`\n=== UPDATING VISUALIZATION ===`);
        console.log(`Total messages: ${normalizedMessages.length}`);
        console.log(`Detected questions: ${questionCount}`);

        this.allMessages = normalizedMessages;

        if (this.allMessages.length === 0) {
            console.log('No messages to visualize');
            this.clear();
            return;
        }

        console.log(`Extracting concepts from ${this.allMessages.length} messages using ChatGPT...`);

        // Extract concepts using ChatGPT
        this.extractedData = await this.extractConcepts(this.allMessages);

        if (!this.extractedData) {
            console.error('Failed to extract concepts from ChatGPT, creating fallback nodes from messages');
            // Create fallback nodes from messages
            this.createFallbackNodes();
        } else {
            const userCount = this.extractedData.userRequestNodes?.length || this.extractedData.UserRequestNodes?.length || 0;
            const aiCount = this.extractedData.aiResponseNodes?.length || this.extractedData.AiResponseNodes?.length || 0;
            const conceptCount = this.extractedData.conceptNodes?.length || this.extractedData.ConceptNodes?.length || 0;
            console.log('Extracted data received:', {
                userRequests: userCount,
                aiResponses: aiCount,
                concepts: conceptCount
            });
            
            // Show feedback if counts don't match
            if (userCount !== questionCount) {
                console.warn(`⚠️ Mismatch: Detected ${questionCount} questions but ChatGPT returned ${userCount} user requests`);
            }
        }

        // Build graph (will use extractedData or fallback)
        this.buildGraph();
        
        console.log(`\n=== GRAPH BUILT ===`);
        console.log(`Total nodes: ${this.nodes.length}`);
        console.log(`Total links: ${this.links.length}`);
        console.log(`Total clusters: ${this.clusters.length}`);

        if (this.nodes.length === 0) {
            console.error('❌ No nodes created! Check extractedData and message structure.');
            return;
        }

        if (this.clusters.length !== questionCount) {
            console.warn(`⚠️ Cluster count mismatch: Expected ${questionCount} clusters but created ${this.clusters.length}`);
        }

        // Render the graph
        this.render();
        
        // Provide feedback to user via console and potentially UI
        console.log(`\n✅ Visualization updated: ${this.clusters.length} question clusters displayed`);
    },

    // Count questions in message history
    countQuestions: function(messages) {
        let questionCount = 0;
        let lastRole = null;
        
        messages.forEach((message) => {
            const role = (message.role || message.Role || '').toLowerCase();
            if (role === 'user') {
                // If previous was also user, it's a new question
                if (lastRole === 'user') {
                    questionCount++;
                } else if (lastRole === null || lastRole === 'assistant') {
                    // First message or after assistant response = new question
                    questionCount++;
                }
            }
            lastRole = role;
        });
        
        return questionCount;
    },

    // Build graph data structure with clusters
    buildGraph: function() {
        this.nodes = [];
        this.links = [];
        this.clusters = [];
        this.manualCluster = null;
        const nodeMap = {};

        if (!this.extractedData) {
            console.warn('No extracted data available');
            return;
        }

        console.log('Building graph from extracted data:', {
            userRequestNodes: this.extractedData.userRequestNodes?.length || 0,
            aiResponseNodes: this.extractedData.aiResponseNodes?.length || 0,
            conceptNodes: this.extractedData.conceptNodes?.length || 0
        });

        // Handle both camelCase and PascalCase property names
        const userRequests = this.extractedData.userRequestNodes || this.extractedData.UserRequestNodes || [];
        const aiResponses = this.extractedData.aiResponseNodes || this.extractedData.AiResponseNodes || [];
        const concepts = this.extractedData.conceptNodes || this.extractedData.ConceptNodes || [];

        console.log(`Processing: ${userRequests.length} user requests, ${aiResponses.length} AI responses, ${concepts.length} concepts`);

        // CRITICAL FIX: Build iteration map directly from message history
        // This ensures we get ALL Q&A pairs, regardless of what ChatGPT returns
        const normalizedMessages = this.normalizeMessages(this.allMessages);
        const messageBasedIterations = this.calculateIterationsFromMessages(normalizedMessages);
        
        console.log(`\n=== MESSAGE-BASED ITERATIONS ===`);
        console.log(`Total messages: ${normalizedMessages.length}`);
        console.log(`Calculated iterations:`, messageBasedIterations.map(i => ({
            iteration: i.iterationIndex,
            role: i.role,
            contentPreview: i.content.substring(0, 50)
        })));

        // Build iteration map from actual message history FIRST
        const iterationMap = {};
        messageBasedIterations.forEach(iter => {
            if (!iterationMap[iter.iterationIndex]) {
                iterationMap[iter.iterationIndex] = {
                    userRequest: null,
                    aiResponse: null,
                    concepts: [],
                    messageIndex: iter.messageIndex
                };
            }
        });
        
        console.log(`Created ${Object.keys(iterationMap).length} iteration slots from message history`);

        // Now match ChatGPT nodes to iterations by content similarity
        // Process user requests - match to message-based iterations
        userRequests.forEach((node, index) => {
            let bestMatch = this.findBestIterationMatch(node, messageBasedIterations, 'user');
            if (bestMatch === -1) {
                // If no match found, assign to first available iteration slot
                const availableSlots = Object.keys(iterationMap).map(k => parseInt(k)).sort((a, b) => a - b);
                bestMatch = availableSlots[index] !== undefined ? availableSlots[index] : index;
                console.warn(`No match found for user request "${node.label || node.Label}", assigning to iteration ${bestMatch}`);
            }
            
            if (!iterationMap[bestMatch]) {
                iterationMap[bestMatch] = { userRequest: null, aiResponse: null, concepts: [] };
            }
            iterationMap[bestMatch].userRequest = node;
            console.log(`✓ Mapped user request "${(node.label || node.Label || 'unknown').substring(0, 40)}" to iteration ${bestMatch}`);
        });

        // Process AI responses - match to message-based iterations
        aiResponses.forEach((node, index) => {
            let bestMatch = this.findBestIterationMatch(node, messageBasedIterations, 'assistant');
            if (bestMatch === -1) {
                // Try to match to iteration that has a user request but no AI response
                const availableSlots = Object.keys(iterationMap)
                    .map(k => parseInt(k))
                    .filter(k => iterationMap[k].userRequest && !iterationMap[k].aiResponse)
                    .sort((a, b) => a - b);
                bestMatch = availableSlots[index] !== undefined ? availableSlots[index] : 
                           Object.keys(iterationMap).map(k => parseInt(k)).sort((a, b) => a - b)[index] || index;
                console.warn(`No match found for AI response "${node.label || node.Label}", assigning to iteration ${bestMatch}`);
            }
            
            if (!iterationMap[bestMatch]) {
                iterationMap[bestMatch] = { userRequest: null, aiResponse: null, concepts: [] };
            }
            iterationMap[bestMatch].aiResponse = node;
            console.log(`✓ Mapped AI response "${(node.label || node.Label || 'unknown').substring(0, 40)}" to iteration ${bestMatch}`);
        });

        // Process concepts - assign to iteration of their AI response
        concepts.forEach((node) => {
            // Try to find which iteration this concept belongs to
            let iterIndex = -1;
            
            // First, try to use the node's iterationIndex if it's valid
            const nodeIterIndex = node.iterationIndex !== undefined ? node.iterationIndex : 
                                 (node.IterationIndex !== undefined ? node.IterationIndex : -1);
            if (nodeIterIndex >= 0 && iterationMap[nodeIterIndex]) {
                iterIndex = nodeIterIndex;
            } else {
                // Find the iteration that has an AI response but no concepts yet (or fewest concepts)
                const slotsWithAI = Object.keys(iterationMap)
                    .map(k => parseInt(k))
                    .filter(k => iterationMap[k].aiResponse)
                    .sort((a, b) => {
                        const aConcepts = iterationMap[a].concepts.length;
                        const bConcepts = iterationMap[b].concepts.length;
                        return aConcepts - bConcepts; // Prefer iterations with fewer concepts
                    });
                iterIndex = slotsWithAI[0] !== undefined ? slotsWithAI[0] : 0;
            }
            
            if (!iterationMap[iterIndex]) {
                iterationMap[iterIndex] = { userRequest: null, aiResponse: null, concepts: [] };
            }
            iterationMap[iterIndex].concepts.push(node);
        });
        
        console.log(`Iteration map has ${Object.keys(iterationMap).length} unique iterations:`, Object.keys(iterationMap).sort((a, b) => parseInt(a) - parseInt(b)));

        // Create clusters from iterations (ensure we process ALL iterations)
        const sortedIterations = Object.keys(iterationMap).map(k => parseInt(k)).sort((a, b) => a - b);
        
        console.log(`Creating ${sortedIterations.length} clusters from iterations:`, sortedIterations);
        
        sortedIterations.forEach((iterIndex, clusterIndex) => {
            const iterData = iterationMap[iterIndex];
            const cluster = {
                id: `cluster_${iterIndex}`,
                iterationIndex: iterIndex,
                userRequest: null,
                aiResponse: null,
                concepts: [],
                nodes: []
            };

            // Add user request node
            if (iterData.userRequest) {
                const nodeData = iterData.userRequest;
                const nodeId = nodeData.id || nodeData.Id || `user_${iterIndex}`;
                const label = nodeData.label || nodeData.Label || `Question ${iterIndex + 1}`;
                const relevance = nodeData.relevance !== undefined ? nodeData.relevance : (nodeData.Relevance !== undefined ? nodeData.Relevance : 0.5);
                
                const newNode = {
                    id: nodeId,
                    label: label,
                    type: 'userRequest',
                    relevance: relevance,
                    iterationIndex: iterIndex,
                    originalText: nodeData.originalText || nodeData.OriginalText || label,
                    size: this.calculateNodeSizeFromRelevance(relevance),
                    clusterId: cluster.id
                };
                this.nodes.push(newNode);
                nodeMap[nodeId] = newNode;
                cluster.userRequest = newNode;
                cluster.nodes.push(newNode);
                console.log(`Created user request node: ${newNode.id} - ${newNode.label}`);
            }

            // Add AI response node
            if (iterData.aiResponse) {
                const nodeData = iterData.aiResponse;
                const nodeId = nodeData.id || nodeData.Id || `ai_${iterIndex}`;
                const label = nodeData.label || nodeData.Label || `Response ${iterIndex + 1}`;
                const relevance = nodeData.relevance !== undefined ? nodeData.relevance : (nodeData.Relevance !== undefined ? nodeData.Relevance : 0.5);
                
                const newNode = {
                    id: nodeId,
                    label: label,
                    type: 'aiResponse',
                    relevance: relevance,
                    iterationIndex: iterIndex,
                    originalText: nodeData.originalText || nodeData.OriginalText || label,
                    size: this.calculateNodeSizeFromRelevance(relevance),
                    clusterId: cluster.id
                };
                this.nodes.push(newNode);
                nodeMap[nodeId] = newNode;
                cluster.aiResponse = newNode;
                cluster.nodes.push(newNode);
                console.log(`Created AI response node: ${newNode.id} - ${newNode.label}`);
            }

            // Add concept nodes (deduplicate by label within cluster)
            const conceptMap = {};
            iterData.concepts.forEach((conceptNode) => {
                const label = conceptNode.label || conceptNode.Label || '';
                if (label && !conceptMap[label]) {
                    const nodeId = conceptNode.id || conceptNode.Id || `concept_${iterIndex}_${label.replace(/\s+/g, '_')}`;
                    const relevance = conceptNode.relevance !== undefined ? conceptNode.relevance : (conceptNode.Relevance !== undefined ? conceptNode.Relevance : 0.5);
                    
                    const newNode = {
                        id: nodeId,
                        label: label,
                        type: 'concept',
                        relevance: relevance,
                        iterationIndex: iterIndex,
                        size: this.calculateNodeSizeFromRelevance(relevance),
                        clusterId: cluster.id
                    };
                    this.nodes.push(newNode);
                    nodeMap[nodeId] = newNode;
                    cluster.concepts.push(newNode);
                    cluster.nodes.push(newNode);
                    conceptMap[label] = newNode;
                    console.log(`Created concept node: ${newNode.id} - ${newNode.label}`);
                }
            });

            // Only add cluster if it has nodes
            if (cluster.nodes.length > 0) {
                this.clusters.push(cluster);
                console.log(`Created cluster ${this.clusters.length - 1} (iteration ${iterIndex}) with ${cluster.nodes.length} nodes`);
            } else {
                console.warn(`Skipping cluster for iteration ${iterIndex} - no nodes created`);
            }
        });
        
        console.log(`Total clusters created: ${this.clusters.length}`);
        console.log(`Total nodes created: ${this.nodes.length}`);
        console.log('Cluster summary:', this.clusters.map(c => ({
            iteration: c.iterationIndex,
            nodes: c.nodes.length,
            hasUser: !!c.userRequest,
            hasAI: !!c.aiResponse,
            concepts: c.concepts.length
        })));

        // Create manual keywords cluster
        if (this.manualKeywords.length > 0) {
            this.manualCluster = {
                id: 'manual_cluster',
                nodes: []
            };

            this.manualKeywords.forEach((keyword) => {
                const nodeId = `manual_${keyword}`;
                const newNode = {
                    id: nodeId,
                    label: keyword,
                    type: 'manualKeyword',
                    relevance: 0.6,
                    iterationIndex: -1,
                    size: this.calculateNodeSizeFromRelevance(0.6),
                    clusterId: this.manualCluster.id
                };
                this.nodes.push(newNode);
                nodeMap[nodeId] = newNode;
                this.manualCluster.nodes.push(newNode);
            });
        }

        // Calculate relevance-based links between ALL nodes
        this.calculateRelevanceLinks(nodeMap);
    },

    // Calculate relevance links between all nodes
    calculateRelevanceLinks: function(nodeMap) {
        const nodes = Object.values(nodeMap);
        
        // Calculate similarity/relevance between nodes
        for (let i = 0; i < nodes.length; i++) {
            for (let j = i + 1; j < nodes.length; j++) {
                const nodeA = nodes[i];
                const nodeB = nodes[j];
                
                // Skip if same cluster (except manual cluster)
                if (nodeA.clusterId === nodeB.clusterId && nodeA.clusterId !== 'manual_cluster') {
                    continue;
                }

                // Calculate relevance strength
                let strength = 0;

                // Same iteration = higher strength
                if (nodeA.iterationIndex === nodeB.iterationIndex && nodeA.iterationIndex >= 0) {
                    strength += 3;
                }

                // Similar relevance scores = connection
                const relevanceDiff = Math.abs(nodeA.relevance - nodeB.relevance);
                strength += (1 - relevanceDiff) * 2;

                // Text similarity (simple word overlap)
                const wordsA = (nodeA.label || '').toLowerCase().split(/\s+/);
                const wordsB = (nodeB.label || '').toLowerCase().split(/\s+/);
                const commonWords = wordsA.filter(w => wordsB.includes(w) && w.length > 2);
                strength += commonWords.length * 1.5;

                // Type-based connections
                if (nodeA.type === 'concept' && nodeB.type === 'concept') {
                    strength += 0.5; // Concepts connect to concepts
                }
                if ((nodeA.type === 'userRequest' && nodeB.type === 'aiResponse') ||
                    (nodeA.type === 'aiResponse' && nodeB.type === 'userRequest')) {
                    strength += 1; // Q&A pairs connect
                }

                // Manual keywords connect to concepts
                if ((nodeA.type === 'manualKeyword' && nodeB.type === 'concept') ||
                    (nodeA.type === 'concept' && nodeB.type === 'manualKeyword')) {
                    strength += 1;
                }

                // Only create link if strength is above threshold
                if (strength > 0.5) {
                    this.links.push({
                        source: nodeA.id,
                        target: nodeB.id,
                        strength: strength,
                        width: this.calculateLinkWidth(strength)
                    });
                }
            }
        }
    },

    // Calculate node size from relevance
    calculateNodeSizeFromRelevance: function(relevance) {
        const [minSize, maxSize] = this.config.nodeSizeRange;
        return minSize + (relevance * (maxSize - minSize));
    },

    // Calculate link width from strength
    calculateLinkWidth: function(strength) {
        const maxStrength = 10; // Normalize to max strength
        const normalized = Math.min(strength / maxStrength, 1);
        const [minWidth, maxWidth] = this.config.linkWidthRange;
        return minWidth + (normalized * (maxWidth - minWidth));
    },

    // Render blueprint-style layout
    render: function() {
        if (!this.svg) {
            console.warn('SVG not initialized');
            return;
        }
        
        if (this.nodes.length === 0) {
            console.warn('No nodes to render');
            return;
        }

        console.log(`Rendering ${this.nodes.length} nodes and ${this.links.length} links`);

        const container = this.svg.select('g.container');
        if (container.empty()) {
            console.error('Container group not found');
            return;
        }
        
        const gridGroup = container.select('g.grid');
        const linksGroup = container.select('g.links');
        const nodesGroup = container.select('g.nodes');
        const labelsGroup = container.select('g.labels');

        const containerEl = document.getElementById(this.containerId);
        const width = containerEl?.clientWidth || 800;
        const height = containerEl?.clientHeight || 600;

        console.log(`Container dimensions: ${width}x${height}`);

        // Clear existing elements
        gridGroup.selectAll('*').remove();
        linksGroup.selectAll('*').remove();
        nodesGroup.selectAll('*').remove();
        labelsGroup.selectAll('*').remove();

        // Draw grid (blueprint style)
        this.drawGrid(gridGroup, width, height);

        // Calculate positions for clusters
        this.calculateClusterPositions(width, height);
        
        // Verify nodes have positions and set defaults if missing
        const nodesWithoutPosition = this.nodes.filter(n => n.x === undefined || n.y === undefined);
        if (nodesWithoutPosition.length > 0) {
            console.warn(`${nodesWithoutPosition.length} nodes missing positions, setting defaults`);
            nodesWithoutPosition.forEach((node, index) => {
                node.x = 100 + (index * 50);
                node.y = 100;
            });
        }
        
        // Log first few nodes for debugging
        if (this.nodes.length > 0) {
            console.log('Sample nodes:', this.nodes.slice(0, 3).map(n => ({ id: n.id, x: n.x, y: n.y, type: n.type })));
        }

        // Draw links
        const link = linksGroup.selectAll('line')
            .data(this.links)
            .enter()
            .append('line')
            .attr('stroke', this.config.colors.link)
            .attr('stroke-opacity', 0.3)
            .attr('stroke-width', d => d.width || 1)
            .style('filter', 'drop-shadow(0 1px 1px rgba(0,0,0,0.1))');

        // Draw nodes
        const node = nodesGroup.selectAll('g.node')
            .data(this.nodes)
            .enter()
            .append('g')
            .attr('class', 'node')
            .attr('transform', d => {
                const x = d.x || 0;
                const y = d.y || 0;
                return `translate(${x},${y})`;
            });

        // Add node circles
        node.append('circle')
            .attr('r', d => d.size)
            .attr('fill', d => this.config.colors[d.type] || this.config.colors.concept)
            .attr('stroke', this.config.colors.blueprint)
            .attr('stroke-width', 2)
            .attr('opacity', 0.9)
            .style('cursor', 'pointer')
            .style('filter', 'drop-shadow(0 2px 4px rgba(0,0,0,0.2))')
            .on('mouseover', function(event, d) {
                d3.select(this)
                    .attr('opacity', 1)
                    .attr('stroke-width', 3);
                
                const tooltip = d3.select('body').append('div')
                    .attr('class', 'keyword-tooltip')
                    .style('position', 'absolute')
                    .style('background', 'rgba(44, 62, 80, 0.95)')
                    .style('color', 'white')
                    .style('padding', '8px 12px')
                    .style('border-radius', '4px')
                    .style('pointer-events', 'none')
                    .style('font-size', '12px')
                    .style('z-index', '1000')
                    .style('border', '1px solid #34495e')
                    .html(`<strong>${d.label}</strong><br/>Type: ${d.type}<br/>Relevance: ${(d.relevance * 100).toFixed(0)}%`);
                
                tooltip.style('left', (event.pageX + 10) + 'px')
                       .style('top', (event.pageY - 10) + 'px');
            })
            .on('mouseout', function(event, d) {
                d3.select(this)
                    .attr('opacity', 0.9)
                    .attr('stroke-width', 2);
                d3.selectAll('.keyword-tooltip').remove();
            });

        // Add node labels
        node.append('text')
            .attr('text-anchor', 'middle')
            .attr('dy', d => d.size + 18)
            .attr('font-size', d => Math.max(10, Math.min(12, d.size / 3)))
            .attr('fill', this.config.colors.blueprint)
            .attr('font-weight', '500')
            .style('pointer-events', 'none')
            .style('user-select', 'none')
            .text(d => {
                const maxLength = 15;
                return d.label.length > maxLength ? d.label.substring(0, maxLength) + '...' : d.label;
            });

        // Update link positions - need to find nodes by ID
        const self = this;
        link.each(function(d) {
            const sourceNode = self.nodes.find(n => n.id === d.source);
            const targetNode = self.nodes.find(n => n.id === d.target);
            if (sourceNode && targetNode) {
                d3.select(this)
                    .attr('x1', sourceNode.x)
                    .attr('y1', sourceNode.y)
                    .attr('x2', targetNode.x)
                    .attr('y2', targetNode.y);
            }
        });

        // Draw cluster boundaries (blueprint style)
        this.drawClusterBoundaries(labelsGroup);

        // Animate nodes
        node.selectAll('circle')
            .attr('r', 0)
            .transition()
            .duration(this.config.animationDuration)
            .attr('r', d => d.size);

        link
            .attr('stroke-opacity', 0)
            .transition()
            .duration(this.config.animationDuration)
            .attr('stroke-opacity', 0.3);
    },

    // Draw grid (blueprint style)
    drawGrid: function(gridGroup, width, height) {
            const gridSize = 50;
            
            // Vertical lines
            for (let x = 0; x <= width; x += gridSize) {
                gridGroup.append('line')
                    .attr('x1', x)
                    .attr('y1', 0)
                    .attr('x2', x)
                    .attr('y2', height)
                    .attr('stroke', this.config.colors.grid)
                    .attr('stroke-width', 0.5)
                    .attr('opacity', 0.2);
            }
            
            // Horizontal lines
            for (let y = 0; y <= height; y += gridSize) {
                gridGroup.append('line')
                    .attr('x1', 0)
                    .attr('y1', y)
                    .attr('x2', width)
                    .attr('y2', y)
                    .attr('stroke', this.config.colors.grid)
                    .attr('stroke-width', 0.5)
                    .attr('opacity', 0.2);
            }
        },

    // Calculate spiral position for nodes around a center
    calculateSpiralPosition: function(centerX, centerY, index, totalNodes, radius = 80, angleOffset = 0) {
        if (totalNodes === 0) return { x: centerX, y: centerY };
        if (totalNodes === 1) return { x: centerX + radius, y: centerY };
        
        // Golden angle for even distribution (137.508 degrees in radians)
        const goldenAngle = 2.399963229728653; // ~137.508 degrees
        const angle = (index * goldenAngle) + angleOffset;
        const spiralRadius = radius * (1 + index * 0.15); // Gradually increase radius
        
        return {
            x: centerX + Math.cos(angle) * spiralRadius,
            y: centerY + Math.sin(angle) * spiralRadius
        };
    },

    // Calculate cluster positions with spiral layout
    calculateClusterPositions: function(width, height) {
        console.log(`Calculating positions for ${this.clusters.length} clusters, ${this.nodes.length} nodes`);
        
        if (this.clusters.length === 0) {
            console.warn('No clusters to position!');
            return;
        }
        
        const padding = this.config.padding;
        const clusterRadius = 200; // Radius of each cluster
        const clusterSpacing = clusterRadius * 2.5; // Space between cluster centers
        
        // Reserve space for manual keywords on the right
        const manualClusterWidth = 250;
        const availableWidth = width - padding.left - padding.right - manualClusterWidth;
        const availableHeight = height - padding.top - padding.bottom;
        
        // Calculate grid layout for clusters
        const clustersPerRow = Math.max(1, Math.floor(availableWidth / clusterSpacing));
        const clustersPerCol = Math.ceil(this.clusters.length / clustersPerRow);
        
        console.log(`Grid layout: ${clustersPerRow} clusters per row, ${clustersPerCol} rows`);
        
        // Center clusters in available space
        const totalWidth = Math.min(clustersPerRow, this.clusters.length) * clusterSpacing;
        const totalHeight = clustersPerCol * clusterSpacing;
        const startX = padding.left + (availableWidth - totalWidth) / 2 + clusterRadius;
        const startY = padding.top + (availableHeight - totalHeight) / 2 + clusterRadius;

        console.log(`Starting position: (${startX}, ${startY}), cluster spacing: ${clusterSpacing}`);

        this.clusters.forEach((cluster, clusterIndex) => {
            const row = Math.floor(clusterIndex / clustersPerRow);
            const col = clusterIndex % clustersPerRow;
            
            // Cluster center position
            const clusterCenterX = startX + (col * clusterSpacing);
            const clusterCenterY = startY + (row * clusterSpacing);
            
            console.log(`Cluster ${clusterIndex} (iteration ${cluster.iterationIndex}) at row ${row}, col ${col}, center (${clusterCenterX}, ${clusterCenterY})`);

            // Position user request at the CENTER of the cluster
            if (cluster.userRequest) {
                cluster.userRequest.x = clusterCenterX;
                cluster.userRequest.y = clusterCenterY;
                console.log(`Positioned userRequest ${cluster.userRequest.id} at center (${clusterCenterX}, ${clusterCenterY})`);
            }

            // Collect all nodes that should spiral around the center (AI response + concepts)
            const spiralNodes = [];
            if (cluster.aiResponse) {
                spiralNodes.push(cluster.aiResponse);
            }
            spiralNodes.push(...cluster.concepts);

            // Position spiral nodes around the center
            spiralNodes.forEach((node, nodeIndex) => {
                const spiralPos = this.calculateSpiralPosition(
                    clusterCenterX,
                    clusterCenterY,
                    nodeIndex,
                    spiralNodes.length,
                    100, // Base radius
                    0 // Angle offset
                );
                node.x = spiralPos.x;
                node.y = spiralPos.y;
                console.log(`Positioned ${node.type} ${node.id} at spiral position (${node.x}, ${node.y})`);
            });
        });

        // Position manual keywords cluster on the right side
        if (this.manualCluster && this.manualCluster.nodes.length > 0) {
            const manualX = width - padding.right - manualClusterWidth + 50;
            const manualY = padding.top + 100;
            const manualSpacing = 70;
            const manualPerRow = 2;

            this.manualCluster.nodes.forEach((node, index) => {
                const rowIndex = Math.floor(index / manualPerRow);
                const colIndex = index % manualPerRow;
                node.x = manualX + (colIndex * 100);
                node.y = manualY + (rowIndex * manualSpacing);
            });
        }
    },

        // Draw cluster boundaries (circular for spiral clusters)
    drawClusterBoundaries: function(labelsGroup) {
        // Draw circular boundaries around Q&A clusters
        this.clusters.forEach((cluster, index) => {
            if (cluster.nodes.length === 0 || !cluster.userRequest) return;

            const centerX = cluster.userRequest.x;
            const centerY = cluster.userRequest.y;
            const clusterRadius = 180; // Radius of the cluster circle

            // Draw circle boundary
            labelsGroup.append('circle')
                .attr('cx', centerX)
                .attr('cy', centerY)
                .attr('r', clusterRadius)
                .attr('fill', 'none')
                .attr('stroke', this.config.colors.blueprint)
                .attr('stroke-width', 1.5)
                .attr('stroke-dasharray', '8,4')
                .attr('opacity', 0.4);

            // Add cluster label above the circle
            labelsGroup.append('text')
                .attr('x', centerX)
                .attr('y', centerY - clusterRadius - 10)
                .attr('font-size', '11px')
                .attr('fill', this.config.colors.blueprint)
                .attr('font-weight', '600')
                .attr('text-anchor', 'middle')
                .text(`Question ${cluster.iterationIndex + 1} (${cluster.nodes.length} nodes)`);
        });
        
        // Add summary text showing total clusters
        if (this.clusters.length > 0) {
            const containerEl = document.getElementById(this.containerId);
            const width = containerEl?.clientWidth || 800;
            
            labelsGroup.append('text')
                .attr('x', 20)
                .attr('y', 20)
                .attr('font-size', '14px')
                .attr('fill', this.config.colors.blueprint)
                .attr('font-weight', '700')
                .attr('text-anchor', 'start')
                .text(`Total Questions: ${this.clusters.length} | Total Nodes: ${this.nodes.length}`);
        }

        // Draw boundary around manual keywords cluster
        if (this.manualCluster && this.manualCluster.nodes.length > 0) {
            const bounds = this.getClusterBounds(this.manualCluster.nodes);
            const padding = 20;

            labelsGroup.append('rect')
                .attr('x', bounds.minX - padding)
                .attr('y', bounds.minY - padding)
                .attr('width', bounds.maxX - bounds.minX + (padding * 2))
                .attr('height', bounds.maxY - bounds.minY + (padding * 2))
                .attr('fill', 'none')
                .attr('stroke', this.config.colors.manualKeyword)
                .attr('stroke-width', 1.5)
                .attr('stroke-dasharray', '5,5')
                .attr('opacity', 0.5);

            labelsGroup.append('text')
                .attr('x', bounds.minX - padding)
                .attr('y', bounds.minY - padding - 5)
                .attr('font-size', '10px')
                .attr('fill', this.config.colors.manualKeyword)
                .attr('font-weight', '600')
                .text('Manual Keywords');
        }
    },

    // Get bounds of a cluster
    getClusterBounds: function(nodes) {
        if (nodes.length === 0) return { minX: 0, minY: 0, maxX: 0, maxY: 0 };

        let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
        nodes.forEach(node => {
            const radius = node.size || 25;
            minX = Math.min(minX, node.x - radius);
            minY = Math.min(minY, node.y - radius);
            maxX = Math.max(maxX, node.x + radius);
            maxY = Math.max(maxY, node.y + radius);
        });
        return { minX, minY, maxX, maxY };
    },

    // Clear the visualization
    clear: function() {
        if (this.svg) {
            this.svg.selectAll('*').remove();
        }
        this.nodes = [];
        this.links = [];
        this.clusters = [];
        this.manualCluster = null;
        this.allMessages = [];
        this.extractedData = null;
        // Keep manualKeywords
    },

    // Refresh visualization
    refresh: function() {
        const savedKeywords = [...this.manualKeywords];
        this.clear();
        this.manualKeywords = savedKeywords;
        if (this.allMessages.length > 0) {
            this.update(this.allMessages);
        }
    },

    // Get statistics
    getStats: function() {
        const stats = {
            totalKeywords: this.nodes.length,
            totalLinks: this.links.length,
            clusters: this.clusters.length,
            manualKeywords: this.manualKeywords.length
        };
        console.log('Returning stats:', stats);
        return stats;
    },

    // Check if visualization has nodes
    hasNodes: function() {
        return this.nodes.length > 0;
    },

    // Check if visualization is rendered
    isRendered: function() {
        return this.svg !== null && this.nodes.length > 0;
    }
};
