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

window.setThemeColors = (primaryColor, secondaryColor) => {
    // Primary colors
    document.documentElement.style.setProperty('--rz-primary', '#096AF2');
    document.documentElement.style.setProperty('--rz-primary-light', '#3A8DF5');
    document.documentElement.style.setProperty('--rz-primary-lighter', 'rgba(9, 106, 242, 0.12)');
    document.documentElement.style.setProperty('--rz-primary-dark', '#0755C1');
    document.documentElement.style.setProperty('--rz-primary-darker', '#054699');
    
    // Secondary colors
    document.documentElement.style.setProperty('--rz-secondary', '#F27A09');
    document.documentElement.style.setProperty('--rz-secondary-light', '#F5953A');
    document.documentElement.style.setProperty('--rz-secondary-lighter', 'rgba(242, 122, 9, 0.12)');
    document.documentElement.style.setProperty('--rz-secondary-dark', '#C16107');
    document.documentElement.style.setProperty('--rz-secondary-darker', '#994D06');
    
    // Update existing properties
    document.documentElement.style.setProperty('--rz-steps-number-selected-background', '#096AF2');
    document.documentElement.style.setProperty('--rz-steps-title-selected-color', '#096AF2');
    document.documentElement.style.setProperty('--rz-info', '#096AF2');
    document.documentElement.style.setProperty('--rz-info-dark', '#0755C1');
    document.documentElement.style.setProperty('--rz-link-color', '#096AF2');
    document.documentElement.style.setProperty('--rz-link-hover-color', '#0755C1');
    document.documentElement.style.setProperty('--rz-on-primary-lighter', '#096AF2');
    document.documentElement.style.setProperty('--rz-on-secondary-lighter', '#F27A09');
};

window.setRzPrimaryColor = (color) => {
    document.documentElement.style.setProperty('--rz-primary', color);
    document.documentElement.style.setProperty('--rz-steps-number-selected-background', color);
    document.documentElement.style.setProperty('--rz-steps-title-selected-color', color);
    document.documentElement.style.setProperty('--rz-info', color);
    document.documentElement.style.setProperty('--rz-info-dark', color);
    document.documentElement.style.setProperty('--rz-link-color', color);
    document.documentElement.style.setProperty('--rz-link-hover-color', color);
    document.documentElement.style.setProperty('--rz-on-primary-lighter', "var(--rz-on-base-dark)");
    document.documentElement.style.setProperty('--rz-primary-lighter', "var(--rz-on-base-light)");
};

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