#!/usr/bin/env python3
"""
Generate plugin icon for LARGERslicer (256x256 PNG)
Design: Professional icon representing slicing/toolpath generation
"""

from PIL import Image, ImageDraw, ImageFont
import os

# Icon specifications
ICON_SIZE = 256
MARGIN = 32
CONTENT_SIZE = ICON_SIZE - (MARGIN * 2)

# Colors (matching existing icon system)
BLACK = "#000000"
BLUE_ACCENT = "#0066CC"
WHITE = "#FFFFFF"

def create_plugin_icon():
    """Create the main plugin icon"""
    # Create image with transparent background
    img = Image.new('RGBA', (ICON_SIZE, ICON_SIZE), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    
    # Draw background circle with subtle gradient effect
    center = ICON_SIZE // 2
    radius = ICON_SIZE // 2 - 8
    
    # Outer circle (subtle border)
    draw.ellipse(
        [center - radius, center - radius, center + radius, center + radius],
        fill=WHITE,
        outline=BLACK,
        width=4
    )
    
    # Inner content area
    content_radius = radius - 16
    draw.ellipse(
        [center - content_radius, center - content_radius, 
         center + content_radius, center + content_radius],
        fill=WHITE,
        outline=BLACK,
        width=3
    )
    
    # Draw slicing layers (representing the slicing concept)
    layer_count = 5
    layer_spacing = (content_radius * 0.6) / layer_count
    
    for i in range(layer_count):
        layer_radius = content_radius * 0.4 + (i * layer_spacing)
        # Alternate between black and blue for visual interest
        color = BLUE_ACCENT if i % 2 == 0 else BLACK
        line_width = 3 if i == layer_count - 1 else 2
        
        # Draw partial arc (top half) to represent layers
        bbox = [
            center - layer_radius, center - layer_radius,
            center + layer_radius, center + layer_radius
        ]
        draw.arc(bbox, start=180, end=0, fill=color, width=line_width)
    
    # Draw toolpath lines (zigzag pattern in center)
    path_points = []
    path_width = content_radius * 0.3
    path_height = content_radius * 0.2
    
    # Create zigzag path
    num_segments = 4
    segment_width = (path_width * 2) / num_segments
    
    for i in range(num_segments + 1):
        x = center - path_width + (i * segment_width)
        y = center + (path_height if i % 2 == 0 else -path_height)
        path_points.append((x, y))
    
    # Draw the path
    if len(path_points) > 1:
        draw.line(path_points, fill=BLUE_ACCENT, width=4, joint='round')
    
    # Add "L" letter in center (for LARGERslicer)
    try:
        # Try to use a system font
        font_size = int(ICON_SIZE * 0.25)
        font = ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", font_size)
    except:
        try:
            font = ImageFont.truetype("/System/Library/Fonts/Arial.ttf", font_size)
        except:
            # Fallback to default font
            font = ImageFont.load_default()
    
    # Draw "L" letter
    text = "L"
    bbox = draw.textbbox((0, 0), text, font=font)
    text_width = bbox[2] - bbox[0]
    text_height = bbox[3] - bbox[1]
    
    text_x = center - text_width // 2
    text_y = center - text_height // 2 - 10
    
    draw.text(
        (text_x, text_y),
        text,
        fill=BLACK,
        font=font,
        anchor="lt"
    )
    
    return img

def main():
    """Generate plugin icon"""
    output_dir = '../Resources'
    os.makedirs(output_dir, exist_ok=True)
    
    print("Generating plugin icon...")
    print(f"Size: {ICON_SIZE}x{ICON_SIZE}px")
    
    icon = create_plugin_icon()
    output_path = os.path.join(output_dir, 'plugin-icon.png')
    icon.save(output_path, 'PNG')
    
    print(f"✓ Plugin icon created: {output_path}")
    
    # Also save to dist directory for packaging
    dist_dir = '../dist'
    os.makedirs(dist_dir, exist_ok=True)
    dist_path = os.path.join(dist_dir, 'icon.png')
    icon.save(dist_path, 'PNG')
    print(f"✓ Plugin icon copied to: {dist_path}")

if __name__ == '__main__':
    main()











