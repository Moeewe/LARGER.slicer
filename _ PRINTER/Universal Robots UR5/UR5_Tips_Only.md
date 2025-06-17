## Practical Tips & Best Practices

These practical hints help avoid common pitfalls when working with the UR5 robot and switching between tools like pens, brushes, or extrusion heads.

### 1. Curve Direction Awareness
Robots follow curve directions from start to end. If multiple curves are connected without adjusting direction, the robot might zigzag between start points. To create efficient back-and-forth paths, flip every second curve using the `Flip Curve` component in Grasshopper.

### 2. Tool Lifting (e.g., Brush or Pen)
When drawing or brushing, it's crucial to lift the tool slightly at the start and end of each curve to avoid dragging across the surface. Add additional points with a Z-offset (10–30 mm upwards) before and after each drawing stroke.

### 3. Consistent Point Spacing
Use `Divide Curve` instead of `Evaluate Curve` for consistent spacing, especially across multiple curves. Since Divide Curve includes endpoints, calculate the division count based on curve length and desired spacing.

### 4. Robot Orientation & Collision Avoidance
In the Robots plugin, tool orientation is vital. Always check the simulation preview: if the robot appears twisted or collides with itself, adjust orientation settings like Elbow, Wrist, and Flip in the respective cluster.

### 5. TCP & Tool Orientation
The starting angles of each joint vary depending on the TCP. A pen might point vertically, while a printhead might be horizontal. When switching tools, check and adjust the default robot pose and orientation to prevent misalignment.

### 6. Tool Length Settings
Tool length must match the physical setup. Lengths are defined in the tool clusters. If incorrect, the robot may drive the tool into the table or the printed surface. Double-check this setting, especially when creating new tools or adjusting the physical setup.

### 7. Unexpected Lifting Movements
Some clusters automatically insert lifting movements between paths or even inside single paths. If unwanted lifting occurs, check these parts of the cluster logic. Usually, they’re grouped and labeled clearly and can be disabled or edited.