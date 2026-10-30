% From the server directory: scasp -s1 tests/half_zombie_example.pl
% Expected: X = attack_human(survivor1).
#include('../half_zombie_behavior.pl').

innocent(player1).
half_zombie(player1).
health(3, player1).
levers_to_win(15).
levers_pulled_count(0).

innocent(survivor1).
health(3, survivor1).
human_visible(survivor1, player1).
distance(survivor1, 1, player1).

?- action(X, player1).
