# DockRotate

Needing deployable arms for antennas on your satellites?

Wanting to try VTOL vehicles with moving engines?

Tired of misaligned solar panel trusses on your space stations?

This plugin can help you.

Docked port pairs can rotate via right-click menu or action groups. ( * )
They can rotate to snap for perfect alignment.
And if you want to uninstall the module, you won't lose any ship, because there's no new parts involved **(this is true for docking ports, but not if you use NodeRotate parts)**. Your station parts will stay aligned, and your satellite arms will stay deployed.

If the rotation step is set to 0, the rotation will be continuous. You can stop it with the "Stop Rotation" right-click menu entry, the "Stop Rotation" action, or Alt-B.

Rotation will malfunction if parts on opposite sides of the rotating joint are connected by struts.

All sorts of decoupling/docking/undocking/klawing/unklawing while moving are fully supported. **Yes, you can build a working Kanadarm.**

**Autostruts** are properly handled.

Forum page: https://forum.kerbalspaceprogram.com/index.php?/topic/170484-dockrotate-rotation-control-on-docking-ports/

( * ) Should work with any port based on ModuleDockingNode.

( ** ) DockRotate can be compatible with **Kerbal Joint Reinforcement**. KJR configuration may need an update: see https://forum.kerbalspaceprogram.com/index.php?/topic/170484-dockrotate&do=findComment&comment=3305721 and https://forum.kerbalspaceprogram.com/index.php?/topic/170484-14-dockrotate&do=findComment&comment=3424519

# Welding

Engineers in EVA can weld docking port pairs.

# NodeRotate

This module can be used to turn any connection node of any physically significant part into a rotating joint.

NodeRotate is intended for modders who want to create new rotating parts. There's an example NodeRotate.cfg file in the distribution, see there for details.

Forum user Psycho\_zs contributed a few motor parts. You can find them in VAB/SPH in the Utility section.

