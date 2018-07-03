#!/usr/bin/perl

use strict;
use warnings;

use LWP::Simple;
use JSON;

my ($user, $repository) = ("peteletroll", "DockRotate");

my $url = "https://api.github.com/repos/$user/$repository/releases";
my $j = get($url)
	or die "$0: can't get $url\n";
$j = from_json($j);
my $total = 0;
foreach my $r (reverse @$j) {
	foreach my $a (@{$r->{assets}}) {
		my $c = $a->{download_count};
		my $d = $a->{updated_at};
		$d =~ s/[A-Z]/ /gs;
		$d =~ s/\s+$//;
		printf "%6d %s %s\n", $c, $d, $a->{name};
		$total += $c;
	}
}
printf "%6d total\n", $total;

