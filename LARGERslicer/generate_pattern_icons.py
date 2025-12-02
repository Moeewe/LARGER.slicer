#!/usr/bin/env python3
"""
Generate professional SVG icons for Bottom Layer Pattern components.
Design: Black with minimal blue accent, showing patterns from top view (like Nautilus plugin).
"""

import os
import xml.etree.ElementTree as ET
from xml.dom import minidom
import subprocess

# Design specifications
ICON_SIZE = 24
CONTENT_MARGIN = 2
CONTENT_SIZE = ICON_SIZE - (CONTENT_MARGIN * 2)
STROKE_WIDTH = 1.5
BLACK = "#000000"
BLUE_ACCENT = "#0066CC"  # Minimal blue accent

# Pattern icons - showing top view of patterns
PATTERN_ICONS = {
    # Bottom Layer Patterns (from top view)
    'BottomLayerSpiralIcon': {
        'paths': [
            # Outer boundary (square with rounded corners)
            'M 4 4 L 20 4 L 20 20 L 4 20 Z',
            # Concentric spirals/circles
            'M 12 6 A 6 6 0 0 1 18 12 A 6 6 0 0 1 12 18 A 6 6 0 0 1 6 12 A 6 6 0 0 1 12 6',
            'M 12 8 A 4 4 0 0 1 16 12 A 4 4 0 0 1 12 16 A 4 4 0 0 1 8 12 A 4 4 0 0 1 12 8',
        ],
        'accent': 'M 12 10 A 2 2 0 0 1 14 12 A 2 2 0 0 1 12 14 A 2 2 0 0 1 10 12 A 2 2 0 0 1 12 10',
        'description': 'Concentric spiral pattern (top view)'
    },
    
    'BottomLayerGridIcon': {
        'paths': [
            # Boundary
            'M 4 4 L 20 4 L 20 20 L 4 20 Z',
            # Zigzag grid lines
            'M 4 8 L 20 8',
            'M 4 12 L 20 12',
            'M 4 16 L 20 16',
            'M 8 4 L 8 20',
            'M 12 4 L 12 20',
            'M 16 4 L 16 20',
        ],
        'accent': 'M 4 12 L 20 12',
        'description': 'Rectangular grid/zigzag pattern (top view)'
    },
    
    'BottomLayerLinesIcon': {
        'paths': [
            # Boundary
            'M 4 4 L 20 4 L 20 20 L 4 20 Z',
            # Parallel lines (horizontal)
            'M 4 8 L 20 8',
            'M 4 12 L 20 12',
            'M 4 16 L 20 16',
        ],
        'accent': 'M 4 12 L 20 12',
        'description': 'Parallel lines pattern (top view)'
    },
    
    'BottomLayerHilbertIcon': {
        'paths': [
            # Boundary
            'M 4 4 L 20 4 L 20 20 L 4 20 Z',
            # Hilbert curve (simplified top view)
            'M 6 6 L 10 6 L 10 10 L 6 10 L 6 14 L 10 14 L 10 18 L 14 18 L 14 14 L 18 14 L 18 10 L 14 10 L 14 6 L 18 6',
        ],
        'accent': 'M 6 6 L 10 6',
        'description': 'Hilbert curve pattern (top view)'
    },
    
    'BottomLayerContourIcon': {
        'paths': [
            # Boundary with undercut (complex shape)
            'M 6 4 L 18 4 L 18 8 L 14 8 L 14 12 L 18 12 L 18 16 L 14 16 L 14 20 L 6 20 L 6 16 L 10 16 L 10 12 L 6 12 Z',
            # Offset curves inside
            'M 8 6 L 16 6 L 16 8 L 12 8 L 12 10 L 16 10 L 16 14 L 12 14 L 12 18 L 8 18 L 8 14 L 10 14 L 10 10 L 8 10 Z',
            'M 10 8 L 14 8 L 14 10 L 12 10 L 12 14 L 14 14 L 14 16 L 12 16 L 12 12 L 10 12 Z',
        ],
        'accent': 'M 8 6 L 16 6',
        'description': 'Contour/offset pattern with undercuts (top view)'
    },
    
    'ContinuousToolpathIcon': {
        'paths': [
            # Complex boundary with undercut
            'M 5 4 L 19 4 L 19 7 L 15 7 L 15 10 L 19 10 L 19 13 L 15 13 L 15 16 L 19 16 L 19 20 L 5 20 L 5 16 L 9 16 L 9 13 L 5 13 L 5 10 L 9 10 L 9 7 L 5 7 Z',
            # Continuous path through offsets
            'M 7 6 L 17 6 L 17 8 L 13 8 L 13 11 L 17 11 L 17 14 L 13 14 L 13 17 L 7 17 L 7 14 L 11 14 L 11 11 L 7 11 Z',
            # Bridge connections (random bridges)
            'M 9 9 L 11 9',
            'M 13 9 L 15 9',
        ],
        'accent': 'M 7 6 L 17 6',
        'description': 'Continuous toolpath with undercuts and bridges (top view)'
    },
    
    'ContinuousPathFromCurvesIcon': {
        'paths': [
            # Boundary
            'M 4 4 L 20 4 L 20 20 L 4 20 Z',
            # Infill lines
            'M 4 8 L 20 8',
            'M 4 12 L 20 12',
            'M 4 16 L 20 16',
            # Boundary segments (highlighted)
            'M 4 4 L 4 8',
            'M 20 4 L 20 8',
            'M 4 12 L 4 16',
            'M 20 12 L 20 16',
        ],
        'accent': 'M 4 12 L 20 12',
        'description': 'Continuous path from curves with infill (top view)'
    },
    
    # Utility components for path processing
    'BridgeCurvesIcon': {
        'paths': [
            # Two separate curves
            'M 4 6 L 10 6 L 10 10 L 4 10 Z',
            'M 14 14 L 20 14 L 20 18 L 14 18 Z',
            # Bridge connection
            'M 10 8 L 14 16',
        ],
        'accent': 'M 10 8 L 14 16',
        'description': 'Bridge curves - connect separate curves'
    },
    
    'SuppressSelfIntersectionsIcon': {
        'paths': [
            # Self-intersecting curve (X shape)
            'M 4 4 L 20 20 M 20 4 L 4 20',
            # Healed curve (two separate paths)
            'M 4 4 L 12 12 M 12 12 L 20 20',
            'M 20 4 L 12 12 M 12 12 L 4 20',
        ],
        'accent': 'M 12 12 L 12 12',
        'description': 'Suppress self-intersections - heal bad paths'
    },
    
    # Toolpath preparation components
    'AlignCurvesIcon': {
        'paths': [
            # Multiple wavy horizontal lines
            'M 4 8 Q 8 6 12 8 T 20 8',
            'M 4 12 Q 8 10 12 12 T 20 12',
            'M 4 16 Q 8 14 12 16 T 20 16',
            # Arrows indicating alignment (all pointing same direction)
            'M 6 8 L 8 8 M 7 7 L 8 8 L 7 9',
            'M 6 12 L 8 12 M 7 11 L 8 12 L 7 13',
            'M 6 16 L 8 16 M 7 15 L 8 16 L 7 17',
        ],
        'accent': 'M 6 8 L 8 8 M 7 7 L 8 8 L 7 9',
        'description': 'Align curves - all pointing same direction'
    },
    
    'AlternateCurvesIcon': {
        'paths': [
            # Multiple wavy horizontal lines
            'M 4 8 Q 8 6 12 8 T 20 8',
            'M 4 12 Q 8 14 12 12 T 20 12',
            'M 4 16 Q 8 18 12 16 T 20 16',
            # Arrows indicating alternation (left-right-left)
            'M 6 8 L 8 8 M 7 7 L 8 8 L 7 9',  # Right arrow
            'M 18 12 L 16 12 M 17 11 L 16 12 L 17 13',  # Left arrow
            'M 6 16 L 8 16 M 7 15 L 8 16 L 7 17',  # Right arrow
        ],
        'accent': 'M 6 8 L 8 8 M 7 7 L 8 8 L 7 9',
        'description': 'Alternate curves - left-right-left pattern'
    },
    
    'JoinOpenContoursIcon': {
        'paths': [
            # Stacked wavy horizontal lines (open contours)
            'M 4 8 Q 8 6 12 8 T 18 8',
            'M 4 12 Q 8 10 12 12 T 18 12',
            'M 4 16 Q 8 14 12 16 T 18 16',
            # Connection lines between contours (transitions)
            'M 18 8 L 20 10 L 18 12',
            'M 18 12 L 20 14 L 18 16',
        ],
        'accent': 'M 18 8 L 20 10 L 18 12',
        'description': 'Join open contours - connect into continuous path'
    },
    
    'ContinuousInfillIcon': {
        'paths': [
            # Outer boundary (irregular shape)
            'M 4 6 Q 6 4 8 6 Q 10 4 12 6 Q 14 4 16 6 Q 18 4 20 6',
            'L 20 10 Q 18 12 20 14 Q 18 16 20 18',
            'L 16 18 Q 14 20 12 18 Q 10 20 8 18 Q 6 20 4 18',
            'L 4 14 Q 6 12 4 10 Z',
            # Concentric offset curves showing continuous path
            'M 6 8 Q 8 6 10 8 Q 12 6 14 8 Q 16 6 18 8',
            'L 18 12 Q 16 14 18 16',
            'L 14 16 Q 12 18 10 16 Q 8 18 6 16',
            'L 6 12 Q 8 10 6 8',
            # Inner complex pattern (showing undercuts/labyrinth)
            'M 8 10 Q 10 8 12 10 Q 14 8 16 10',
            'L 16 14 Q 14 16 12 14 Q 10 16 8 14 Z',
            'M 10 12 L 14 12',
            'M 12 10 L 12 14',
        ],
        'accent': 'M 8 10 Q 10 8 12 10 Q 14 8 16 10',
        'description': 'Continuous infill with complex geometry handling'
    },
    
    'InfillFermatSpiralsIcon': {
        'paths': [
            # Outer boundary (irregular shape)
            'M 4 6 Q 6 4 8 6 Q 10 4 12 6 Q 14 4 16 6 Q 18 4 20 6',
            'L 20 10 Q 18 12 20 14 Q 18 16 20 18',
            'L 16 18 Q 14 20 12 18 Q 10 20 8 18 Q 6 20 4 18',
            'L 4 14 Q 6 12 4 10 Z',
            # Fermat spiral curves (smooth, continuous)
            'M 12 8 Q 10 10 8 12 Q 10 14 12 16 Q 14 14 16 12 Q 14 10 12 8',
            'M 12 10 Q 11 11 10 12 Q 11 13 12 14 Q 13 13 14 12 Q 13 11 12 10',
        ],
        'accent': 'M 12 8 Q 10 10 8 12 Q 10 14 12 16',
        'description': 'Connected Fermat Spirals - smooth continuous curves'
    },
    
    'EulerianPathIcon': {
        'paths': [
            # Closed boundary curve
            'M 6 6 L 18 6 L 18 18 L 6 18 Z',
            # Offset curves inside (concentric)
            'M 8 8 L 16 8 L 16 16 L 8 16 Z',
            'M 10 10 L 14 10 L 14 14 L 10 14 Z',
            # Continuous path through all (Eulerian circuit)
            'M 6 6 L 18 6 L 18 8 L 8 8 L 8 16 L 16 16 L 16 10 L 10 10 L 10 14 L 14 14 L 14 12 L 12 12 L 12 18 L 6 18 Z',
        ],
        'accent': 'M 6 6 L 18 6',
        'description': 'Eulerian path - continuous circuit through all segments'
    },
}

def create_svg_icon(name, config):
    """Create a professional SVG icon"""
    # Create SVG root
    svg = ET.Element('svg', {
        'xmlns': 'http://www.w3.org/2000/svg',
        'width': str(ICON_SIZE),
        'height': str(ICON_SIZE),
        'viewBox': f'0 0 {ICON_SIZE} {ICON_SIZE}'
    })
    
    # Add style definitions
    defs = ET.SubElement(svg, 'defs')
    style = ET.SubElement(defs, 'style')
    style.text = f'''
        .icon-main {{ stroke: {BLACK}; stroke-width: {STROKE_WIDTH}; fill: none; stroke-linecap: round; stroke-linejoin: round; }}
        .icon-accent {{ stroke: {BLUE_ACCENT}; stroke-width: {STROKE_WIDTH}; fill: none; stroke-linecap: round; stroke-linejoin: round; }}
        .icon-fill {{ fill: {BLACK}; stroke: none; }}
    '''
    
    # Add drop shadow filter
    filter_elem = ET.SubElement(defs, 'filter', {'id': 'shadow'})
    ET.SubElement(filter_elem, 'feGaussianBlur', {'in': 'SourceAlpha', 'stdDeviation': '1', 'result': 'blur'})
    ET.SubElement(filter_elem, 'feOffset', {'in': 'blur', 'dx': '1', 'dy': '1', 'result': 'offsetBlur'})
    feComponentTransfer = ET.SubElement(filter_elem, 'feComponentTransfer')
    ET.SubElement(feComponentTransfer, 'feFuncA', {'type': 'linear', 'slope': '0.33'})
    feMerge = ET.SubElement(filter_elem, 'feMerge')
    ET.SubElement(feMerge, 'feMergeNode', {'in': 'offsetBlur'})
    ET.SubElement(feMerge, 'feMergeNode', {'in': 'SourceGraphic'})
    
    # Create group for main icon
    g_main = ET.SubElement(svg, 'g', {'class': 'icon-main', 'filter': 'url(#shadow)'})
    
    # Add all paths
    if 'paths' in config:
        for path_str in config['paths']:
            path_elem = ET.SubElement(g_main, 'path', {'d': path_str})
    
    # Add accent in blue (minimal)
    if 'accent' in config:
        g_accent = ET.SubElement(svg, 'g', {'class': 'icon-accent'})
        accent_path = ET.SubElement(g_accent, 'path', {'d': config['accent']})
    
    return svg

def prettify_svg(elem):
    """Return a pretty-printed XML string for the Element"""
    rough_string = ET.tostring(elem, 'unicode')
    reparsed = minidom.parseString(rough_string)
    return reparsed.toprettyxml(indent="  ")

def convert_svg_to_png(svg_path, png_path):
    """Convert SVG to PNG using cairosvg"""
    try:
        subprocess.run(['cairosvg', svg_path, '-o', png_path, '-W', str(ICON_SIZE), '-H', str(ICON_SIZE)], 
                      check=True, capture_output=True)
        return True
    except (subprocess.CalledProcessError, FileNotFoundError):
        print(f"  Warning: cairosvg not found, skipping PNG conversion for {svg_path}")
        return False

def main():
    """Generate all pattern icons"""
    output_dir = 'Resources'
    os.makedirs(output_dir, exist_ok=True)
    
    print("Generating professional pattern icons (top view)...")
    print(f"Design: Black ({BLACK}) with minimal blue accent ({BLUE_ACCENT})")
    print(f"Size: {ICON_SIZE}x{ICON_SIZE}px, Stroke: {STROKE_WIDTH}px\n")
    
    svg_count = 0
    png_count = 0
    
    for name, config in PATTERN_ICONS.items():
        print(f"  Creating {name}...")
        svg = create_svg_icon(name, config)
        
        # Save SVG
        svg_string = prettify_svg(svg)
        # Remove XML declaration for cleaner output
        svg_string = '\n'.join(svg_string.split('\n')[1:])
        
        svg_path = os.path.join(output_dir, f'{name}.svg')
        with open(svg_path, 'w', encoding='utf-8') as f:
            f.write(svg_string)
        svg_count += 1
        
        # Convert to PNG
        png_path = os.path.join(output_dir, f'{name}.png')
        if convert_svg_to_png(svg_path, png_path):
            png_count += 1
    
    print(f"\n✓ Generated {svg_count} SVG icons in {output_dir}/")
    if png_count > 0:
        print(f"✓ Converted {png_count} icons to PNG")
    else:
        print("\nNote: Install cairosvg to convert SVG to PNG:")
        print("  pip install cairosvg")

if __name__ == '__main__':
    main()

