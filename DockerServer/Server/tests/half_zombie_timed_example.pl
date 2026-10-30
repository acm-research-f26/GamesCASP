% Expected: X = attack_human(survivor1), despite a nearby zombie.
#include('../half_zombie_timed_behavior.pl').

innocent(player1).
half_zombie(player1).
health(3, player1).
levers_to_win(15).
levers_pulled_count(0).

current_time(115).
bite_window_started_at(100, player1).

innocent(survivor1).
health(3, survivor1).
human_visible(survivor1, player1).
distance(survivor1, 1, player1).

zombie(z).
distance(z, 2, player1).

?- action(X, player1).
