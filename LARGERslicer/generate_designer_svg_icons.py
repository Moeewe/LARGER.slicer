#!/usr/bin/env python3
"""
Generate professional SVG icons for Grasshopper components.
Design: Black with minimal blue accent, designer quality, following Grasshopper guidelines.
"""

import os
import xml.etree.ElementTree as ET
from xml.dom import minidom

# Design specifications
ICON_SIZE = 24
CONTENT_MARGIN = 2
CONTENT_SIZE = ICON_SIZE - (CONTENT_MARGIN * 2)
STROKE_WIDTH = 1.5
BLACK = "#000000"
BLUE_ACCENT = "#0066CC"  # Minimal blue accent

# Component definitions with SVG paths
ICONS = {
    # CNC
    'CNCProgramIcon': {
        'path': 'M 4 12 L 8 6 L 12 12 L 16 6 L 20 12',
        'accent': 'M 4 18 L 8 12 L 12 18 L 16 12 L 20 18',
        'description': 'Zigzag pattern for CNC toolpath'
    },
    
    # DXR Processing
    'DXRGeneratorIcon': {
        'path': 'M 6 4 L 6 20 L 18 20 L 18 16 L 14 12 L 14 4 Z M 14 12 L 18 12',
        'accent': 'M 18 10 L 20 10',
        'description': 'File/document with export arrow'
    },
    'DXRPostprocessorIcon': {
        'path': 'M 4 6 L 4 18 L 10 18 L 10 6 Z M 14 6 L 14 18 L 20 18 L 20 6 Z',
        'accent': 'M 10 12 L 14 10 L 14 14 Z',
        'description': 'Code conversion (two blocks with arrow)'
    },
    'MachineSettingsIcon': {
        'path': 'M 12 4 L 14 8 L 18 9 L 15 12 L 15.5 16 L 12 14.5 L 8.5 16 L 9 12 L 6 9 L 10 8 Z',
        'accent': 'M 12 10 L 12 14',
        'description': 'Gear/settings icon'
    },
    
    # Utilities
    'SafeIcon': {
        'path': 'M 6 4 L 6 20 L 18 20 L 18 4 Z M 8 6 L 16 6 L 16 12 L 8 12 Z',
        'accent': 'M 8 14 L 16 14 L 16 16 L 8 16 Z',
        'description': 'Floppy disk/save icon'
    },
    'DateTimestampIcon': {
        'path': 'M 12 4 A 8 8 0 1 1 12 20 A 8 8 0 1 1 12 4 Z M 12 8 L 12 12 L 16 12',
        'accent': 'M 12 12 L 12 12',
        'description': 'Clock face'
    },
    'DesktopPathIcon': {
        'path': 'M 6 6 L 10 6 L 12 8 L 18 8 L 18 20 L 6 20 Z',
        'accent': 'M 6 6 L 10 6 L 10 8 L 6 8 Z',
        'description': 'Folder icon'
    },
    'CustomPreviewLineweightsIcon': {
        'path': 'M 4 8 L 20 8 M 4 12 L 20 12 M 4 16 L 20 16',
        'accent': 'M 4 12 L 20 12',
        'description': 'Three lines with different weights'
    },
    'FeedrateIcon': {
        'path': 'M 12 4 A 8 8 0 0 1 12 20 M 12 4 L 16 8',
        'accent': 'M 12 4 L 16 8',
        'description': 'Speedometer/semicircle with needle'
    },
    'RTreeClosestPointIcon': {
        'path': 'M 6 6 L 18 6 L 12 18 Z M 8 10 L 16 10 L 12 14 Z',
        'accent': 'M 12 12 L 12 12',
        'description': 'Spatial search/triangle hierarchy'
    },
    'RTreeSortIcon': {
        'path': 'M 12 4 L 8 10 L 16 10 Z M 8 10 L 6 16 L 10 16 Z M 16 10 L 14 16 L 18 16 Z',
        'accent': 'M 12 4 L 8 10',
        'description': 'Tree structure for sorting'
    },
    'StreamFreezeIcon': {
        'path': 'M 8 4 L 8 20 M 16 4 L 16 20',
        'accent': 'M 8 4 L 8 20',
        'description': 'Pause/freeze symbol (two vertical bars)'
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
    ET.SubElement(filter_elem, 'feMerge').extend([
        ET.Element('feMergeNode', {'in': 'offsetBlur'}),
        ET.Element('feMergeNode', {'in': 'SourceGraphic'})
    ])
    
    # Create group for main icon
    g_main = ET.SubElement(svg, 'g', {'class': 'icon-main', 'filter': 'url(#shadow)'})
    
    # Add main path
    if 'path' in config:
        path_elem = ET.SubElement(g_main, 'path', {'d': config['path']})
    
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

def main():
    """Generate all SVG icons"""
    output_dir = 'Resources'
    os.makedirs(output_dir, exist_ok=True)
    
    print("Generating professional SVG icons...")
    print(f"Design: Black ({BLACK}) with minimal blue accent ({BLUE_ACCENT})")
    print(f"Size: {ICON_SIZE}x{ICON_SIZE}px, Stroke: {STROKE_WIDTH}px\n")
    
    for name, config in ICONS.items():
        print(f"  Creating {name}.svg...")
        svg = create_svg_icon(name, config)
        
        # Save SVG
        svg_string = prettify_svg(svg)
        # Remove XML declaration for cleaner output
        svg_string = '\n'.join(svg_string.split('\n')[1:])
        
        with open(os.path.join(output_dir, f'{name}.svg'), 'w', encoding='utf-8') as f:
            f.write(svg_string)
    
    print(f"\n✓ Generated {len(ICONS)} SVG icons in {output_dir}/")
    print("\nNext step: Convert SVG to PNG using cairosvg or similar tool")

if __name__ == '__main__':
    main()

