#!/usr/bin/perl

use strict;
use warnings;

use LWP::Simple;
use JSON;

my ($user, $repository) = ("peteletroll", "DockRotate");

my $j = from_json(get("https://api.github.com/repos/$user/$repository/releases"));
foreach my $r (@$j) {
	foreach my $a (@{$r->{assets}}) {
		printf "%6d %s\n", $a->{download_count}, $a->{name};
	}
}

