let vantaEffect = null;

window.initVantaBackground = (elementId, options) => {
    if (vantaEffect) {
        //console.log("VantaBackground already enabled, skipping..");
        return;
    }
    
    // Default to dark theme colors
    let vantaColor = 0x096af2;
    let vantaBgColor = 0x000000;
    
    // If isDarkTheme is explicitly provided in options, use it
    if (options && options.isDarkTheme === false) {
        vantaColor = 0xffffff; // Lighter blue
        vantaBgColor = 0xFF096AF2; // Light background
    }
    
    // Remove isDarkTheme from options before passing to VANTA
    if (options && options.isDarkTheme !== undefined) {
        const { isDarkTheme, ...cleanOptions } = options;
        options = cleanOptions;
    }
    
    vantaEffect = VANTA.NET({
        el: "#" + elementId,
        minHeight: 200.00,
        minWidth: 200.00,
        scale: 1.00,
        scaleMobile: 1.00,
        color: vantaColor,
        backgroundColor: vantaBgColor,
        ...options
    });
    //console.log("VantaBackground enabled!");
};

window.destroyVantaBackground = () => {
    if (vantaEffect) {
        vantaEffect.destroy();
        vantaEffect = null;
        //console.log("VantaBackground disabled!");
    }
};

window.scrollToElement = (elementId) => {
    const element = document.getElementById(elementId);
    if (element) {
        element.scrollIntoView({ behavior: 'smooth', block: 'end' });
    }
}

window.scrollToTop = () => {
    window.scrollTo({ top: 0, behavior: 'smooth' });
}



window.applyTablistStyle = (element) => {
    if (element) {
        element.style.backgroundColor = 'var(--rz-body-background-color)';
        element.style.padding = '10px';
        element.style.borderRadius = '4px';
    }
};

window.preventWindowClose = (message) => {
    window.onbeforeunload = (e) => {
        e = e || window.event;
        // For IE and Firefox
        if (e) {
            e.returnValue = message;
        }
        // For Safari
        return message;
    };
};

window.allowWindowClose = () => {
    window.onbeforeunload = null;
};

// Function to update Vanta background with explicit theme setting
window.updateVantaForTheme = (isDarkTheme) => {
    if (vantaEffect) {
        // Destroy current effect
        vantaEffect.destroy();
        vantaEffect = null;
        
        // Reinitialize with proper colors based on the theme parameter
        window.initVantaBackground("vanta-background", {
            mouseControls: false,
            touchControls: false,
            gyroControls: false,
            isDarkTheme: isDarkTheme
        });
    }
    
    // Also update glass card styles based on theme
    window.updateGlassCardStyles(isDarkTheme);
};

// Function to update glass card styles based on theme
window.updateGlassCardStyles = (isDarkTheme) => {
    // Get the primary RGB value
    const style = getComputedStyle(document.documentElement);
    const primaryRgb = style.getPropertyValue('--rz-primary-rgb') || '9, 106, 242'; // Default fallback
    
    if (isDarkTheme) {
        // Dark theme styles
        document.documentElement.style.setProperty('--glass-card-bg-gradient-start', `rgba(${primaryRgb}, 0.3)`);
        document.documentElement.style.setProperty('--glass-card-bg-gradient-end', `rgba(${primaryRgb}, 0.2)`);
        document.documentElement.style.setProperty('--glass-card-box-shadow', `rgba(${primaryRgb}, 0.3)`);
        document.documentElement.style.setProperty('--glass-card-border', 'rgba(255,255,255,0.25)');
        document.documentElement.style.setProperty('--glass-content-bg', 'rgba(255,255,255,0.08)');
        document.documentElement.style.setProperty('--glass-badge-bg', `rgba(${primaryRgb}, 0.25)`);
        document.documentElement.style.setProperty('--impower-logo', 'white');
        document.documentElement.style.setProperty('--impower-logo-alt', '#096af2');
    } else {
        // Light theme styles - use solid white background
        document.documentElement.style.setProperty('--glass-card-bg-gradient-start', 'white');
        document.documentElement.style.setProperty('--glass-card-bg-gradient-end', 'white');
        document.documentElement.style.setProperty('--glass-card-box-shadow', 'rgba(0,0,0,0.08)');
        document.documentElement.style.setProperty('--glass-card-border', 'rgba(0,0,0,0.15)');
        document.documentElement.style.setProperty('--glass-content-bg', 'white');
        document.documentElement.style.setProperty('--glass-badge-bg', `rgba(${primaryRgb}, 0.1)`);
        document.documentElement.style.setProperty('--impower-logo', 'white');
        document.documentElement.style.setProperty('--impower-logo-alt', 'black');
    }
};

window.getTimezone = () => {
    return Intl.DateTimeFormat().resolvedOptions().timeZone;
};

window.isMobileDevice = () => {
    // Check if the user agent string indicates a mobile device
    const userAgent = navigator.userAgent || navigator.vendor || window.opera;
    const mobileRegex = /Android|webOS|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini/i;
    
    // Return true if it's a mobile device
    return mobileRegex.test(userAgent);
};