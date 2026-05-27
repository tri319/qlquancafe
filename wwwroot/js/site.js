// Custom Cursor Glow Trail (Global)
$(document).ready(function() {
    // Create container if not exists
    if ($('#cursor-trail-container').length === 0) {
        $('body').append('<div id="cursor-trail-container"></div>');
    }

    const trailContainer = document.getElementById('cursor-trail-container');
    const dots = [];
    const numDots = 10;

    for (let i = 0; i < numDots; i++) {
        let dot = document.createElement('div');
        dot.className = 'trail-dot';
        let size = 10 - i;
        dot.style.width = size + 'px';
        dot.style.height = size + 'px';
        dot.style.opacity = 1 - (i / numDots);
        trailContainer.appendChild(dot);
        dots.push({ x: 0, y: 0, node: dot });
    }

    let mouseX = 0, mouseY = 0;

    document.addEventListener('mousemove', function(e) {
        mouseX = e.clientX;
        mouseY = e.clientY;
    });

    function renderTrail() {
        let x = mouseX;
        let y = mouseY;

        dots.forEach(function(dot, index) {
            let nextDot = dots[index + 1] || dots[0];
            dot.x = x;
            dot.y = y;
            dot.node.style.left = x + 'px';
            dot.node.style.top = y + 'px';
            x += (nextDot.x - x) * 0.4;
            y += (nextDot.y - y) * 0.4;
        });
        requestAnimationFrame(renderTrail);
    }
    renderTrail();
});
