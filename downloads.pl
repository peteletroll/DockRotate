#!/usr/bin/perl

use strict;
use warnings;

use URI;
use LWP::Simple;
use JSON;

my ($user, $repository) = ("peteletroll", "DockRotate");

my $url = URI->new("https://api.github.com/repos/$user/$repository/releases");
my $page = 1;
my $j = [ ];
for (;;) {
	$url->query_form(page => $page);
	# warn "GET $url\n";
	my $jp = get($url)
		or die "$0: can't get $url\n";
	$jp = from_json($jp);
	@$jp or last;
	push @$j, @$jp;
	$page++;
}

my $total = 0;
foreach my $r (reverse @$j) {
	foreach my $a (@{$r->{assets}}) {
		my $c = $a->{download_count};
		my $d = $a->{updated_at};
		$d =~ s/[A-Z]/ /gs;
		$d =~ s/^\s+//;
		$d =~ s/\s+$//;
		$d =~ s/\s+/ /g;
		printf "%6d   %s   %s\n", $c, $d, $a->{name};
		$total += $c;
	}
}
printf "%6d total - %s\n", $total, $url;

