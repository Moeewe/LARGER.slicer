#!/usr/bin/env python3
"""
Generate professional SVG icons for Grasshopper components.
Design: Black with minimal blue accent, designer quality, following Grasshopper guidelines.
Based on reference image analysis - clean, minimalist, professional.
"""

import os

# Design specifications
ICON_SIZE = 24
CONTENT_MARGIN = 2
STROKE_WIDTH = 1.5
BLACK = "#000000"
BLUE_ACCENT = "#0066CC"  # Minimal blue accent

# Professional SVG icon definitions
ICONS = {
    # CNC - Zigzag pattern
    'CNCProgramIcon': {
        'svg': f'''<svg xmlns="http://www.w3.org/2000/svg" width="{ICON_SIZE}" height="{ICON_SIZE}" viewBox="0 0 {ICON_SIZE} {ICON_SIZE}">
  <defs>
    <filter id="shadow">
      <feGaussianBlur in="SourceAlpha" stdDeviation="1" result="blur"/>
      <feOffset in="blur" dx="1" dy="1" result="offsetBlur"/>
      <feComponentTransfer>
        <feFuncA type="linear" slope="0.33"/>
      </feComponentTransfer>
      <feMerge>
        <feMergeNode in="offsetBlur"/>
        <feMergeNode in="SourceGraphic"/>
      </feMerge>
    </filter>
  </defs>
  <g filter="url(#shadow)">
    <path d="M 4 12 L 8 6 L 12 12 L 16 6 L 20 12" stroke="{BLACK}" stroke-width="{STROKE_WIDTH}" fill="none" stroke-linecap="round" stroke-linejoin="round"/>
    <path d="M 4 18 L 8 12 L 12 18 L 16 12 L 20 18" stroke="{BLUE_ACCENT}" stroke-width="{STROKE_WIDTH}" fill="none" stroke-linecap="round" stroke-linejoin="round"/>
  </g>
</svg>'''
    },
    
    # DXR Generator - File with export arrow
    'DXRGeneratorIcon': {
        'svg': f'''<svg xmlns="http://www.w3.org/2000/svg" width="{ICON_SIZE}" height="{ICON_SIZE}" viewBox="0 0 {ICON_SIZE} {ICON_SIZE}">
  <defs>
    <filter id="shadow">
      <feGaussianBlur in="SourceAlpha" stdDeviation="1" result="blur"/>
      <feOffset in="blur" dx="1" dy="1" result="offsetBlur"/>
      <feComponentTransfer>
        <feFuncA type="linear" slope="0.33"/>
      </feComponentTransfer>
      <feMerge>
        <feMergeNode in="offsetBlur"/>
        <feMergeNode in="SourceGraphic"/>
      </feMerge>
    </filter>
  </defs>
  <g filter="url(#shadow)">
    <rect x="6" y="4" width="12" height="16" stroke="{BLACK}" stroke-width="{STROKE_WIDTH}" fill="none" stroke-linejoin="round"/>
    <path d="M 14 4 L 14 8 L 18 8" stroke="{BLACK}" stroke-width="{STROKE_WIDTH}" fill="none" stroke-linecap="round" stroke-linejoin="round"/>
    <path d="M 18 10 L 20 10 M 18 10 L 19 9 M 18 10 L 19 11" stroke="{BLUE_ACCENT}" stroke-width="{STROKE_WIDTH}" fill="none" stroke-linecap="round"/>
  </g>
</svg>'''
    },
    
    # DXR Postprocessor - Code conversion
    'DXRPostprocessorIcon': {
        'svg': f'''<svg xmlns="http://www.w3.org/2000/svg" width="{ICON_SIZE}" height="{ICON_SIZE}" viewBox="0 0 {ICON_SIZE} {ICON_SIZE}">
  <defs>
    <filter id="shadow">
      <feGaussianBlur in="SourceAlpha" stdDeviation="1" result="blur"/>
      <feOffset in="blur" dx="1" dy="1" result="offsetBlur"/>
      <feComponentTransfer>
        <feFuncA type="linear" slope="0.33"/>
      </feComponentTransfer>
      <feMerge>
        <feMergeNode in="offsetBlur"/>
        <feMergeNode in="SourceGraphic"/>
      </feMerge>
    </filter>
  </defs>
  <g filter="url(#shadow)">
    <rect x="4" y="6" width="6" height="12" stroke="{BLACK}" stroke-width="{STROKE_WIDTH}" fill="none" stroke-linejoin="round"/>
    <line x1="5" y1="9" x2="9" y2="9" stroke="{BLACK}" stroke-width="1" stroke-linecap="round"/>
    <line x1="5" y1="12" x2="8" y2="12" stroke="{BLACK}" stroke-width="1" stroke-linecap="round"/>
    <line x1="5" y1="15" x2="9" y2="15" stroke="{BLACK}" stroke-width="1" stroke-linecap="round"/>
    <path d="M 10 12 L 14 10 L 14 14 Z" stroke="{BLUE_ACCENT}" stroke-width="{STROKE_WIDTH}" fill="none" stroke-linecap="round" stroke-linejoin="round"/>
    <rect x="16" y="6" width="6" height="12" stroke="{BLACK}" stroke-width="{STROKE_WIDTH}" fill="none" stroke-linejoin="round"/>
    <line x1="17" y1="9" x2="21" y2="9" stroke="{BLACK}" stroke-width="1" stroke-linecap="round"/>
    <line x1="17" y1="12" x2="20" y2="12" stroke="{BLACK}" stroke-width="1" stroke-linecap="round"/>
    <line x1="17" y1="15" x2="21" y2="15" stroke="{BLACK}" stroke-width="1" stroke-linecap="round"/>
  </g>
</svg>'''
    },
    
    # Machine Settings - Gear
    'MachineSettingsIcon': {
        'svg': f'''<svg xmlns="http://www.w3.org/2000/svg" width="{ICON_SIZE}" height="{ICON_SIZE}" viewBox="0 0 {ICON_SIZE} {ICON_SIZE}">
  <defs>
    <filter id="shadow">
      <feGaussianBlur in="SourceAlpha" stdDeviation="1" result="blur"/>
      <feOffset in="blur" dx="1" dy="1" result="offsetBlur"/>
      <feComponentTransfer>
        <feFuncA type="linear" slope="0.33"/>
      </feComponentTransfer>
      <feMerge>
        <feMergeNode in="offsetBlur"/>
        <feMergeNode in="SourceGraphic"/>
      </feMerge>
    </filter>
  </defs>
  <g filter="url(#shadow)">
    <circle cx="12" cy="12" r="7" stroke="{BLACK}" stroke-width="{STROKE_WIDTH}" fill="none"/>
    <circle cx="12" cy="12" r="2.5" stroke="{BLACK}" stroke-width="{STROKE_WIDTH}" fill="none"/>
    <path d="M 12 4 L 12 6 M 12 18 L 12 20 M 4 12 L 6 12 M 18 12 L 20 12 M 6.5 6.5 L 8 8 M 17.5 17.5 L 16 16 M 6.5 17.5 L 8 16 M 17.5 6.5 L 16 8" stroke="{BLACK}" stroke-width="{STROKE_WIDTH}" stroke-linecap="round"/>
    <path d="M 12 10 L 12 14" stroke="{BLUE_ACCENT}" stroke-width="{STROKE_WIDTH}" stroke-linecap="round"/>
  </g>
</svg>'''
    },
    
    # Safe - Floppy disk
    'SafeIcon': {
        'svg': f'''<svg xmlns="http://www.w3.org/2000/svg" width="{ICON_SIZE}" height="{ICON_SIZE}" viewBox="0 0 {ICON_SIZE} {ICON_SIZE}">
  <defs>
    <filter id="shadow">
      <feGaussianBlur in="SourceAlpha" stdDeviation="1" result="blur"/>
      <feOffset in="blur" dx="1" dy="1" result="offsetBlur"/>
      <feComponentTransfer>
        <feFuncA type="linear" slope="0.33"/>
      </feComponentTransfer>
      <feMerge>
        <feMergeNode in="offsetBlur"/>
        <feMergeNode in="SourceGraphic"/>
      </feMerge>
    </filter>
  </defs>
  <g filter="url(#shadow)">
    <rect x="6" y="4" width="12" height="16" stroke="{BLACK}" stroke-width="{STROKE_WIDTH}" fill="none" stroke-linejoin="round"/>
    <rect x="8" y="6" width="8" height="6" stroke="{BLACK}" stroke-width="{STROKE_WIDTH}" fill="{BLACK}"/>
    <rect x="8" y="14" width="8" height="2" stroke="{BLACK}" stroke-width="1" fill="none" stroke-linecap="round"/>
    <path d="M 8 14 L 8 14" stroke="{BLUE_ACCENT}" stroke-width="2" stroke-linecap="round"/>
  </g>
</svg>'''
    },
    
    # Date Timestamp - Clock
    'DateTimestampIcon': {
        'svg': f'''<svg xmlns="http://www.w3.org/2000/svg" width="{ICON_SIZE}" height="{ICON_SIZE}" viewBox="0 0 {ICON_SIZE} {ICON_SIZE}">
  <defs>
    <filter id="shadow">
      <feGaussianBlur in="SourceAlpha" stdDeviation="1" result="blur"/>
      <feOffset in="blur" dx="1" dy="1" result="offsetBlur"/>
      <feComponentTransfer>
        <feFuncA type="linear" slope="0.33"/>
      </feComponentTransfer>
      <feMerge>
        <feMergeNode in="offsetBlur"/>
        <feMergeNode in="SourceGraphic"/>
      </feMerge>
    </filter>
  </defs>
  <g filter="url(#shadow)">
    <circle cx="12" cy="12" r="8" stroke="{BLACK}" stroke-width="{STROKE_WIDTH}" fill="none"/>
    <circle cx="12" cy="12" r="1" fill="{BLACK}"/>
    <line x1="12" y1="12" x2="12" y2="8" stroke="{BLACK}" stroke-width="{STROKE_WIDTH}" stroke-linecap="round"/>
    <line x1="12" y1="12" x2="16" y2="12" stroke="{BLUE_ACCENT}" stroke-width="{STROKE_WIDTH}" stroke-linecap="round"/>
  </g>
</svg>'''
    },
    
    # Desktop Path - Folder
    'DesktopPathIcon': {
        'svg': f'''<svg xmlns="http://www.w3.org/2000/svg" width="{ICON_SIZE}" height="{ICON_SIZE}" viewBox="0 0 {ICON_SIZE} {ICON_SIZE}">
  <defs>
    <filter id="shadow">
      <feGaussianBlur in="SourceAlpha" stdDeviation="1" result="blur"/>
      <feOffset in="blur" dx="1" dy="1" result="offsetBlur"/>
      <feComponentTransfer>
        <feFuncA type="linear" slope="0.33"/>
      </feComponentTransfer>
      <feMerge>
        <feMergeNode in="offsetBlur"/>
        <feMergeNode in="SourceGraphic"/>
      </feMerge>
    </filter>
  </defs>
  <g filter="url(#shadow)">
    <path d="M 6 6 L 10 6 L 12 8 L 18 8 L 18 20 L 6 20 Z" stroke="{BLACK}" stroke-width="{STROKE_WIDTH}" fill="none" stroke-linejoin="round"/>
    <path d="M 6 6 L 10 6 L 10 8 L 6 8 Z" stroke="{BLUE_ACCENT}" stroke-width="{STROKE_WIDTH}" fill="none" stroke-linejoin="round"/>
  </g>
</svg>'''
    },
    
    # Custom Preview Lineweights - Three lines
    'CustomPreviewLineweightsIcon': {
        'svg': f'''<svg xmlns="http://www.w3.org/2000/svg" width="{ICON_SIZE}" height="{ICON_SIZE}" viewBox="0 0 {ICON_SIZE} {ICON_SIZE}">
  <defs>
    <filter id="shadow">
      <feGaussianBlur in="SourceAlpha" stdDeviation="1" result="blur"/>
      <feOffset in="blur" dx="1" dy="1" result="offsetBlur"/>
      <feComponentTransfer>
        <feFuncA type="linear" slope="0.33"/>
      </feComponentTransfer>
      <feMerge>
        <feMergeNode in="offsetBlur"/>
        <feMergeNode in="SourceGraphic"/>
      </feMerge>
    </filter>
  </defs>
  <g filter="url(#shadow)">
    <line x1="4" y1="8" x2="20" y2="8" stroke="{BLACK}" stroke-width="1" stroke-linecap="round"/>
    <line x1="4" y1="12" x2="20" y2="12" stroke="{BLUE_ACCENT}" stroke-width="{STROKE_WIDTH}" stroke-linecap="round"/>
    <line x1="4" y1="16" x2="20" y2="16" stroke="{BLACK}" stroke-width="2.5" stroke-linecap="round"/>
  </g>
</svg>'''
    },
    
    # Feedrate - Speedometer
    'FeedrateIcon': {
        'svg': f'''<svg xmlns="http://www.w3.org/2000/svg" width="{ICON_SIZE}" height="{ICON_SIZE}" viewBox="0 0 {ICON_SIZE} {ICON_SIZE}">
  <defs>
    <filter id="shadow">
      <feGaussianBlur in="SourceAlpha" stdDeviation="1" result="blur"/>
      <feOffset in="blur" dx="1" dy="1" result="offsetBlur"/>
      <feComponentTransfer>
        <feFuncA type="linear" slope="0.33"/>
      </feComponentTransfer>
      <feMerge>
        <feMergeNode in="offsetBlur"/>
        <feMergeNode in="SourceGraphic"/>
      </feMerge>
    </filter>
  </defs>
  <g filter="url(#shadow)">
    <path d="M 4 12 A 8 8 0 0 1 20 12" stroke="{BLACK}" stroke-width="{STROKE_WIDTH}" fill="none" stroke-linecap="round"/>
    <circle cx="12" cy="12" r="1" fill="{BLACK}"/>
    <line x1="12" y1="12" x2="16" y2="8" stroke="{BLUE_ACCENT}" stroke-width="{STROKE_WIDTH}" stroke-linecap="round"/>
    <circle cx="4" cy="12" r="0.8" fill="{BLACK}"/>
    <circle cx="20" cy="12" r="0.8" fill="{BLACK}"/>
  </g>
</svg>'''
    },
    
    # RTree Closest Point - Spatial search
    'RTreeClosestPointIcon': {
        'svg': f'''<svg xmlns="http://www.w3.org/2000/svg" width="{ICON_SIZE}" height="{ICON_SIZE}" viewBox="0 0 {ICON_SIZE} {ICON_SIZE}">
  <defs>
    <filter id="shadow">
      <feGaussianBlur in="SourceAlpha" stdDeviation="1" result="blur"/>
      <feOffset in="blur" dx="1" dy="1" result="offsetBlur"/>
      <feComponentTransfer>
        <feFuncA type="linear" slope="0.33"/>
      </feComponentTransfer>
      <feMerge>
        <feMergeNode in="offsetBlur"/>
        <feMergeNode in="SourceGraphic"/>
      </feMerge>
    </filter>
  </defs>
  <g filter="url(#shadow)">
    <circle cx="8" cy="8" r="1.5" fill="{BLACK}"/>
    <circle cx="16" cy="8" r="1.5" fill="{BLACK}"/>
    <circle cx="8" cy="16" r="1.5" fill="{BLACK}"/>
    <circle cx="16" cy="16" r="1.5" fill="{BLACK}"/>
    <circle cx="12" cy="12" r="2.5" stroke="{BLUE_ACCENT}" stroke-width="{STROKE_WIDTH}" fill="none"/>
    <line x1="12" y1="12" x2="8" y2="8" stroke="{BLACK}" stroke-width="1" stroke-linecap="round" stroke-dasharray="1,1"/>
    <line x1="12" y1="12" x2="16" y2="8" stroke="{BLACK}" stroke-width="1" stroke-linecap="round" stroke-dasharray="1,1"/>
    <line x1="12" y1="12" x2="8" y2="16" stroke="{BLACK}" stroke-width="1" stroke-linecap="round" stroke-dasharray="1,1"/>
    <line x1="12" y1="12" x2="16" y2="16" stroke="{BLACK}" stroke-width="1" stroke-linecap="round" stroke-dasharray="1,1"/>
  </g>
</svg>'''
    },
    
    # RTree Sort - Tree structure
    'RTreeSortIcon': {
        'svg': f'''<svg xmlns="http://www.w3.org/2000/svg" width="{ICON_SIZE}" height="{ICON_SIZE}" viewBox="0 0 {ICON_SIZE} {ICON_SIZE}">
  <defs>
    <filter id="shadow">
      <feGaussianBlur in="SourceAlpha" stdDeviation="1" result="blur"/>
      <feOffset in="blur" dx="1" dy="1" result="offsetBlur"/>
      <feComponentTransfer>
        <feFuncA type="linear" slope="0.33"/>
      </feComponentTransfer>
      <feMerge>
        <feMergeNode in="offsetBlur"/>
        <feMergeNode in="SourceGraphic"/>
      </feMerge>
    </filter>
  </defs>
  <g filter="url(#shadow)">
    <line x1="12" y1="4" x2="8" y2="10" stroke="{BLACK}" stroke-width="{STROKE_WIDTH}" stroke-linecap="round"/>
    <line x1="12" y1="4" x2="16" y2="10" stroke="{BLACK}" stroke-width="{STROKE_WIDTH}" stroke-linecap="round"/>
    <line x1="8" y1="10" x2="6" y2="16" stroke="{BLACK}" stroke-width="{STROKE_WIDTH}" stroke-linecap="round"/>
    <line x1="8" y1="10" x2="10" y2="16" stroke="{BLACK}" stroke-width="{STROKE_WIDTH}" stroke-linecap="round"/>
    <line x1="16" y1="10" x2="14" y2="16" stroke="{BLUE_ACCENT}" stroke-width="{STROKE_WIDTH}" stroke-linecap="round"/>
    <line x1="16" y1="10" x2="18" y2="16" stroke="{BLACK}" stroke-width="{STROKE_WIDTH}" stroke-linecap="round"/>
    <circle cx="12" cy="4" r="1.5" fill="{BLACK}"/>
    <circle cx="8" cy="10" r="1.5" fill="{BLACK}"/>
    <circle cx="16" cy="10" r="1.5" fill="{BLACK}"/>
    <circle cx="6" cy="16" r="1.5" fill="{BLACK}"/>
    <circle cx="10" cy="16" r="1.5" fill="{BLACK}"/>
    <circle cx="14" cy="16" r="1.5" fill="{BLUE_ACCENT}"/>
    <circle cx="18" cy="16" r="1.5" fill="{BLACK}"/>
  </g>
</svg>'''
    },
    
    # Stream Freeze - Pause bars
    'StreamFreezeIcon': {
        'svg': f'''<svg xmlns="http://www.w3.org/2000/svg" width="{ICON_SIZE}" height="{ICON_SIZE}" viewBox="0 0 {ICON_SIZE} {ICON_SIZE}">
  <defs>
    <filter id="shadow">
      <feGaussianBlur in="SourceAlpha" stdDeviation="1" result="blur"/>
      <feOffset in="blur" dx="1" dy="1" result="offsetBlur"/>
      <feComponentTransfer>
        <feFuncA type="linear" slope="0.33"/>
      </feComponentTransfer>
      <feMerge>
        <feMergeNode in="offsetBlur"/>
        <feMergeNode in="SourceGraphic"/>
      </feMerge>
    </filter>
  </defs>
  <g filter="url(#shadow)">
    <rect x="7" y="4" width="3" height="16" stroke="{BLACK}" stroke-width="{STROKE_WIDTH}" fill="none" stroke-linecap="round"/>
    <rect x="14" y="4" width="3" height="16" stroke="{BLUE_ACCENT}" stroke-width="{STROKE_WIDTH}" fill="none" stroke-linecap="round"/>
  </g>
</svg>'''
    },
}

def main():
    """Generate all SVG icons"""
    output_dir = 'Resources'
    os.makedirs(output_dir, exist_ok=True)
    
    print("Generating professional SVG icons...")
    print(f"Design: Black ({BLACK}) with minimal blue accent ({BLUE_ACCENT})")
    print(f"Size: {ICON_SIZE}x{ICON_SIZE}px, Stroke: {STROKE_WIDTH}px\n")
    
    for name, config in ICONS.items():
        print(f"  Creating {name}.svg...")
        with open(os.path.join(output_dir, f'{name}.svg'), 'w', encoding='utf-8') as f:
            f.write(config['svg'])
    
    print(f"\n✓ Generated {len(ICONS)} SVG icons in {output_dir}/")
    print("\nConverting SVG to PNG...")
    
    # Convert to PNG using cairosvg if available
    try:
        import cairosvg
        for name in ICONS.keys():
            svg_path = os.path.join(output_dir, f'{name}.svg')
            png_path = os.path.join(output_dir, f'{name}.png')
            cairosvg.svg2png(url=svg_path, write_to=png_path, output_width=ICON_SIZE, output_height=ICON_SIZE)
            print(f"  Converted {name}.svg → {name}.png")
        print(f"\n✓ Converted all icons to PNG")
    except ImportError:
        print("\n⚠ cairosvg not installed. Install with: pip install cairosvg")
        print("SVG files are ready. You can convert them manually or install cairosvg.")

if __name__ == '__main__':
    main()

