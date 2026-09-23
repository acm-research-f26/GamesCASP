

% NOTE: Run this file together with each scenario file.
% Example:
% scasp -i guard.pl scenario1.pl
%
% Then query:
% notices(guard1, player).
%
% For the justification tree:
% scasp --tree -i guard.pl scenario1.pl

% Guard notices the player if they are in range 
% AND not hidden AND nothing is blocking their view

notices(Guard, Player) :-
    distance(Guard, Player, Distance),
    vision_range(Guard, Range),
    Distance =< Range,
    not hidden(Player),
    not blocked(Guard, Player).
