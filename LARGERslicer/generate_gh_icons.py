#!/usr/bin/env python3
"""
Generate Grasshopper-compliant icons following official design guidelines:
- 24x24 pixels
- 2 pixel margin (content area: 20x20)
- 1-2 pixel line width
- Pixel-aligned
- Darker outline colors (not pure black)
- Drop shadow: 1px right/down, 2px blur, 33% opacity
"""

from PIL import Image, ImageDraw, ImageFilter
import os

# Color palette (consistent across icons)
COLORS = {
    'blue': '#0066CC',      # Primary accent
    'blue_dark': '#004499', # Outline
    'coral': '#FF6B6B',     # Export actions
    'coral_dark': '#CC5555',
    'turquoise': '#4ECDC4',  # Settings/Config
    'turquoise_dark': '#3BA39C',
    'yellow': '#FFD93D',     # Processing
    'yellow_dark': '#CCB030',
    'gray': '#95A5A6',       # Utilities
    'gray_dark': '#7F8C8D',
    'white': '#FFFFFF',
    'black': '#000000',
}

# Icon definitions with design concepts
ICONS = {
    # CNC
    'CNCProgramIcon.png': {
        'concept': 'zigzag_pattern',
        'color': COLORS['blue'],
        'color_dark': COLORS['blue_dark'],
    },
    
    # DXR Processing
    'DXRGeneratorIcon.png': {
        'concept': 'file_export',
        'color': COLORS['coral'],
        'color_dark': COLORS['coral_dark'],
    },
    'DXRPostprocessorIcon.png': {
        'concept': 'code_conversion',
        'color': COLORS['coral'],
        'color_dark': COLORS['coral_dark'],
    },
    'MachineSettingsIcon.png': {
        'concept': 'settings_gear',
        'color': COLORS['turquoise'],
        'color_dark': COLORS['turquoise_dark'],
    },
    
    # Utilities
    'SafeIcon.png': {
        'concept': 'file_save',
        'color': COLORS['gray'],
        'color_dark': COLORS['gray_dark'],
    },
    'DateTimestampIcon.png': {
        'concept': 'clock_time',
        'color': COLORS['gray'],
        'color_dark': COLORS['gray_dark'],
    },
    'DesktopPathIcon.png': {
        'concept': 'folder_desktop',
        'color': COLORS['gray'],
        'color_dark': COLORS['gray_dark'],
    },
    'CustomPreviewLineweightsIcon.png': {
        'concept': 'line_weight',
        'color': COLORS['gray'],
        'color_dark': COLORS['gray_dark'],
    },
    'FeedrateIcon.png': {
        'concept': 'speed_calculation',
        'color': COLORS['gray'],
        'color_dark': COLORS['gray_dark'],
    },
    'RTreeClosestPointIcon.png': {
        'concept': 'spatial_search',
        'color': COLORS['gray'],
        'color_dark': COLORS['gray_dark'],
    },
    'RTreeSortIcon.png': {
        'concept': 'sorting_tree',
        'color': COLORS['gray'],
        'color_dark': COLORS['gray_dark'],
    },
    'StreamFreezeIcon.png': {
        'concept': 'freeze_pause',
        'color': COLORS['gray'],
        'color_dark': COLORS['gray_dark'],
    },
}

def hex_to_rgb(hex_color):
    """Convert hex color to RGB tuple"""
    hex_color = hex_color.lstrip('#')
    return tuple(int(hex_color[i:i+2], 16) for i in (0, 2, 4))

def create_drop_shadow(draw, shape_func, offset=(1, 1), blur=2, opacity=0.33):
    """Create drop shadow effect"""
    # Create temporary image for shadow
    shadow_img = Image.new('RGBA', (26, 26), (0, 0, 0, 0))
    shadow_draw = ImageDraw.Draw(shadow_img)
    
    # Draw shape shifted
    shape_func(shadow_draw, offset=offset, color=(0, 0, 0, int(255 * opacity)))
    
    # Apply blur
    shadow_img = shadow_img.filter(ImageFilter.GaussianBlur(radius=blur))
    
    return shadow_img

def draw_zigzag_pattern(draw, color, color_dark, offset=(0, 0)):
    """Draw zigzag pattern for CNC icon"""
    x0, y0 = 2 + offset[0], 12 + offset[1]
    x1, y1 = 8 + offset[0], 6 + offset[1]
    x2, y2 = 14 + offset[0], 12 + offset[1]
    x3, y3 = 20 + offset[0], 6 + offset[1]
    
    # Fill
    points = [(x0, y0), (x1, y1), (x2, y2), (x3, y3), (x3, y0 + 6), (x2, y2 + 6), (x1, y1 + 6), (x0, y0 + 6)]
    draw.polygon(points, fill=color)
    # Outline
    draw.line([(x0, y0), (x1, y1), (x2, y2), (x3, y3)], fill=color_dark, width=2)
    draw.line([(x0, y0 + 6), (x1, y1 + 6), (x2, y2 + 6), (x3, y3 + 6)], fill=color_dark, width=2)
    draw.line([(x0, y0), (x0, y0 + 6)], fill=color_dark, width=2)
    draw.line([(x3, y3), (x3, y3 + 6)], fill=color_dark, width=2)

def draw_file_export(draw, color, color_dark, offset=(0, 0)):
    """Draw file/document with export arrow"""
    x, y = 2 + offset[0], 2 + offset[1]
    # Document
    draw.rectangle([x + 2, y + 2, x + 14, y + 18], fill=COLORS['white'], outline=color_dark, width=2)
    # Folded corner
    draw.polygon([(x + 10, y + 2), (x + 14, y + 2), (x + 14, y + 6)], fill=color_dark)
    # Export arrow
    arrow_x = x + 16
    arrow_y = y + 10
    draw.polygon([(arrow_x, arrow_y), (arrow_x + 4, arrow_y - 2), (arrow_x + 4, arrow_y + 2)], fill=color)
    draw.line([(arrow_x + 4, arrow_y), (arrow_x + 6, arrow_y)], fill=color, width=2)

def draw_code_conversion(draw, color, color_dark, offset=(0, 0)):
    """Draw code conversion (GCode to DXR)"""
    x, y = 2 + offset[0], 2 + offset[1]
    # Left code block
    draw.rectangle([x + 2, y + 4, x + 8, y + 18], fill=color, outline=color_dark, width=2)
    draw.line([x + 3, y + 7, x + 7, y + 7], fill=color_dark, width=1)
    draw.line([x + 3, y + 10, x + 6, y + 10], fill=color_dark, width=1)
    draw.line([x + 3, y + 13, x + 7, y + 13], fill=color_dark, width=1)
    # Arrow
    draw.polygon([(x + 10, y + 10), (x + 14, y + 8), (x + 14, y + 12)], fill=color)
    # Right code block
    draw.rectangle([x + 16, y + 4, x + 22, y + 18], fill=color, outline=color_dark, width=2)
    draw.line([x + 17, y + 7, x + 21, y + 7], fill=color_dark, width=1)
    draw.line([x + 17, y + 10, x + 20, y + 10], fill=color_dark, width=1)
    draw.line([x + 17, y + 13, x + 21, y + 13], fill=color_dark, width=1)

def draw_settings_gear(draw, color, color_dark, offset=(0, 0)):
    """Draw settings gear icon"""
    cx, cy = 12 + offset[0], 12 + offset[1]
    radius = 7
    
    # Gear teeth
    for i in range(8):
        angle = i * 45
        import math
        rad = math.radians(angle)
        x1 = cx + (radius + 2) * math.cos(rad)
        y1 = cy + (radius + 2) * math.sin(rad)
        x2 = cx + radius * math.cos(rad)
        y2 = cy + radius * math.sin(rad)
        draw.line([(x1, y1), (x2, y2)], fill=color_dark, width=2)
    
    # Center circle
    draw.ellipse([cx - radius, cy - radius, cx + radius, cy + radius], 
                 fill=color, outline=color_dark, width=2)
    # Inner circle
    draw.ellipse([cx - 3, cy - 3, cx + 3, cy + 3], 
                 fill=color_dark, outline=color_dark, width=1)

def draw_file_save(draw, color, color_dark, offset=(0, 0)):
    """Draw save icon (floppy disk)"""
    x, y = 2 + offset[0], 2 + offset[1]
    # Disk body
    draw.rectangle([x + 4, y + 4, x + 20, y + 20], fill=color, outline=color_dark, width=2)
    # Label area
    draw.rectangle([x + 6, y + 6, x + 18, y + 12], fill=color_dark)
    # Metal slider
    draw.rectangle([x + 6, y + 14, x + 18, y + 16], fill=COLORS['white'], outline=color_dark, width=1)

def draw_clock_time(draw, color, color_dark, offset=(0, 0)):
    """Draw clock icon"""
    cx, cy = 12 + offset[0], 12 + offset[1]
    radius = 8
    # Clock face
    draw.ellipse([cx - radius, cy - radius, cx + radius, cy + radius], 
                 fill=COLORS['white'], outline=color_dark, width=2)
    # Hour hand
    draw.line([cx, cy, cx, cy - 4], fill=color_dark, width=2)
    # Minute hand
    draw.line([cx, cy, cx + 5, cy], fill=color_dark, width=2)
    # Center dot
    draw.ellipse([cx - 1, cy - 1, cx + 1, cy + 1], fill=color_dark)

def draw_folder_desktop(draw, color, color_dark, offset=(0, 0)):
    """Draw folder/desktop icon"""
    x, y = 2 + offset[0], 2 + offset[1]
    # Folder tab
    draw.polygon([(x + 4, y + 4), (x + 8, y + 4), (x + 10, y + 6), (x + 20, y + 6)], 
                 fill=color_dark)
    # Folder body
    draw.rectangle([x + 4, y + 6, x + 20, y + 20], fill=color, outline=color_dark, width=2)

def draw_line_weight(draw, color, color_dark, offset=(0, 0)):
    """Draw line weight icon"""
    x, y = 2 + offset[0], 2 + offset[1]
    # Three lines with different weights
    draw.line([x + 4, y + 6, x + 20, y + 6], fill=color_dark, width=1)
    draw.line([x + 4, y + 12, x + 20, y + 12], fill=color_dark, width=2)
    draw.line([x + 4, y + 18, x + 20, y + 18], fill=color_dark, width=3)

def draw_speed_calculation(draw, color, color_dark, offset=(0, 0)):
    """Draw speed/feedrate icon"""
    cx, cy = 12 + offset[0], 12 + offset[1]
    # Speedometer arc
    import math
    for i in range(5):
        angle = 45 + i * 18
        rad = math.radians(angle)
        x = cx + 8 * math.cos(rad)
        y = cy - 8 * math.sin(rad)
        draw.ellipse([x - 1, y - 1, x + 1, y + 1], fill=color)
    # Needle
    draw.line([cx, cy, cx + 6, cy - 6], fill=color_dark, width=2)

def draw_spatial_search(draw, color, color_dark, offset=(0, 0)):
    """Draw spatial search (RTree closest point)"""
    cx, cy = 12 + offset[0], 12 + offset[1]
    # Reference points (small dots)
    points = [(cx - 6, cy - 4), (cx + 4, cy - 6), (cx - 4, cy + 6), (cx + 6, cy + 4)]
    for px, py in points:
        draw.ellipse([px - 1, py - 1, px + 1, py + 1], fill=color)
    # Search point (larger)
    draw.ellipse([cx - 2, cy - 2, cx + 2, cy + 2], fill=color_dark)
    # Connection lines
    for px, py in points:
        draw.line([cx, cy, px, py], fill=color_dark, width=1)

def draw_sorting_tree(draw, color, color_dark, offset=(0, 0)):
    """Draw sorting tree icon"""
    x, y = 2 + offset[0], 2 + offset[1]
    # Tree structure
    draw.line([x + 12, y + 4, x + 8, y + 10], fill=color_dark, width=2)
    draw.line([x + 12, y + 4, x + 16, y + 10], fill=color_dark, width=2)
    draw.line([x + 8, y + 10, x + 6, y + 16], fill=color_dark, width=2)
    draw.line([x + 8, y + 10, x + 10, y + 16], fill=color_dark, width=2)
    draw.line([x + 16, y + 10, x + 14, y + 16], fill=color_dark, width=2)
    draw.line([x + 16, y + 10, x + 18, y + 16], fill=color_dark, width=2)
    # Nodes
    for px, py in [(x + 12, y + 4), (x + 8, y + 10), (x + 16, y + 10), 
                    (x + 6, y + 16), (x + 10, y + 16), (x + 14, y + 16), (x + 18, y + 16)]:
        draw.ellipse([px - 1.5, py - 1.5, px + 1.5, py + 1.5], fill=color)

def draw_freeze_pause(draw, color, color_dark, offset=(0, 0)):
    """Draw freeze/pause icon"""
    x, y = 2 + offset[0], 2 + offset[1]
    # Pause symbol (two vertical bars)
    draw.rectangle([x + 6, y + 4, x + 9, y + 20], fill=color)
    draw.rectangle([x + 15, y + 4, x + 18, y + 20], fill=color)
    # Ice crystals
    for i in range(3):
        px = x + 4 + i * 6
        py = y + 12
        draw.polygon([(px, py - 2), (px + 2, py), (px, py + 2), (px - 2, py)], 
                     fill=color_dark)

def generate_icon(filename, config):
    """Generate a single icon following Grasshopper guidelines"""
    # Create 24x24 image with transparent background
    img = Image.new('RGBA', (24, 24), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    
    color = hex_to_rgb(config['color'])
    color_dark = hex_to_rgb(config['color_dark'])
    
    # Draw shadow first (on separate layer)
    shadow_img = Image.new('RGBA', (24, 24), (0, 0, 0, 0))
    shadow_draw = ImageDraw.Draw(shadow_img)
    
    # Draw shape function based on concept
    concept = config['concept']
    shape_funcs = {
        'zigzag_pattern': lambda d, off: draw_zigzag_pattern(d, color, color_dark, off),
        'file_export': lambda d, off: draw_file_export(d, color, color_dark, off),
        'code_conversion': lambda d, off: draw_code_conversion(d, color, color_dark, off),
        'settings_gear': lambda d, off: draw_settings_gear(d, color, color_dark, off),
        'file_save': lambda d, off: draw_file_save(d, color, color_dark, off),
        'clock_time': lambda d, off: draw_clock_time(d, color, color_dark, off),
        'folder_desktop': lambda d, off: draw_folder_desktop(d, color, color_dark, off),
        'line_weight': lambda d, off: draw_line_weight(d, color, color_dark, off),
        'speed_calculation': lambda d, off: draw_speed_calculation(d, color, color_dark, off),
        'spatial_search': lambda d, off: draw_spatial_search(d, color, color_dark, off),
        'sorting_tree': lambda d, off: draw_sorting_tree(d, color, color_dark, off),
        'freeze_pause': lambda d, off: draw_freeze_pause(d, color, color_dark, off),
    }
    
    if concept in shape_funcs:
        # Draw shadow
        shape_funcs[concept](shadow_draw, (1, 1))
        # Apply blur to shadow
        shadow_img = shadow_img.filter(ImageFilter.GaussianBlur(radius=2))
        # Composite shadow with 33% opacity
        shadow_alpha = shadow_img.split()[3]
        shadow_alpha = shadow_alpha.point(lambda p: int(p * 0.33))
        shadow_img.putalpha(shadow_alpha)
        img = Image.alpha_composite(img, shadow_img)
        
        # Draw main shape
        draw = ImageDraw.Draw(img)
        shape_funcs[concept](draw, (0, 0))
    
    return img

def main():
    """Generate all icons"""
    output_dir = 'Resources'
    os.makedirs(output_dir, exist_ok=True)
    
    print("Generating Grasshopper-compliant icons...")
    for filename, config in ICONS.items():
        print(f"  Creating {filename}...")
        icon = generate_icon(filename, config)
        icon.save(os.path.join(output_dir, filename), 'PNG')
    
    print(f"\n✓ Generated {len(ICONS)} icons in {output_dir}/")
    print("\nIcon specifications:")
    print("  - Size: 24x24 pixels")
    print("  - Margin: 2 pixels (content area: 20x20)")
    print("  - Line width: 1-2 pixels")
    print("  - Drop shadow: 1px offset, 2px blur, 33% opacity")
    print("  - Format: PNG with transparent background")

if __name__ == '__main__':
    main()

