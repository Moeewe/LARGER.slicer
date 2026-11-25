# Third-party notices

## Universal Robots arm models (ur_description)

The 3D models of the UR3, UR3e, UR5, UR5e, UR10 and UR10e arms used by UR Cell and UR Simulation come from
**ur_description** by Universal Robots A/S: https://github.com/UniversalRobots/Universal_Robots_ROS2_Description
(the collision meshes of `meshes/<model>/collision`, placed by `config/<model>/default_kinematics.yaml` and
`visual_parameters.yaml`). They were converted to millimetres and stored in `Resources/UR/*.urm`; nothing else
was changed. These models are licensed under the BSD-3-Clause license below (the repository's README names
only other models, UR8 Long, UR15, UR18, UR20, UR30 and the G-series, as Graphical Documentation under separate
terms; those are not used here). LARGER.slicer is not made, endorsed or supported by Universal Robots.

```
Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

   * Redistributions of source code must retain the above copyright
     notice, this list of conditions and the following disclaimer.

   * Redistributions in binary form must reproduce the above copyright
     notice, this list of conditions and the following disclaimer in the
     documentation and/or other materials provided with the distribution.

   * Neither the name of the copyright holder nor the names of its
     contributors may be used to endorse or promote products derived from
     this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE
LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
POSSIBILITY OF SUCH DAMAGE.
```
