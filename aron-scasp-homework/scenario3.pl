



% The player is in range of the guards view
distance(guard1, player, 5).

% The guard can see with max range of 10
vision_range(guard1, 10).

% Check: is something blocking guard1 view
blocked(guard1, player).