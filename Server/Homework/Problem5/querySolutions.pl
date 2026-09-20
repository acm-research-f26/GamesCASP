?- reachable(vault).

?- opens(door3).

?- reachable(treasure).

?- -safe(D). % specifically -safe(D) instead of alarm_triggered

?- -safe(door2). % lack of evidence of unsafety vs. evidence of safety.

    % "Doesn't open" feels right if a door is "classically unsafe"
    % because a triggered alarm usually indicates that the 
    % defense mechanism engages and the criminal should not
    % be able to access any more rooms. However, -safe is only
    % derived from when an alarm is triggered, and has no
    % relationship with blocked (sealed) code.