#!/bin/bash

name=DockRotate

ksphome=~/KSP/KSP_linux

force=0
zipname=''
while getopts fz: opt
do
	case $opt in
	f)
		force=1
		;;
	z)
		zipname="$OPTARG"
		;;
	*)
		exit 1
		;;
	esac
done
shift `expr $OPTIND - 1`

cd `dirname $0` || exit 1

# [assembly: AssemblyVersion ("1.3.1.1")]
version=`sed -n 's/.*\<AssemblyVersion\>.*"\([^"]\+\)".*/\1/p' "$name/Properties/AssemblyInfo.cs"`
if [ "$version" = "" ]
then
	echo "ABORTING: can't find assembly version number" 1>&2
	exit 1
fi

fileversion=`sed -n 's/.*\<AssemblyFileVersion\>.*"\([^"]\+\)".*/\1/p' "$name/Properties/AssemblyInfo.cs"`
if [ "$fileversion" != "$version" ]
then
	echo "ABORTING: version incoherency: $version != $fileversion" 1>&2
	exit 1
fi


dll="$name/bin/Release/$name.dll"
debugdll="$name/bin/Debug/$name.dll"

dllname=`basename $dll`

foundbadspacing=0
for f in `find . -name \*.cs`
do
	if grep -Hn '	 \| 	\|[^	]	\|[ 	]$\|\<class\>.*[ 	]:' $f 1>&2
	then
		foundbadspacing=1
	fi
done
if [ $foundbadspacing -ne 0 ]
then
	echo "ABORTING: found bad spacing, see above" 1>&2
	exit 1
fi
echo source spacing is ok

foundnewer=0
for f in `find . -name \*.cs`
do
	for d in $dll $debugdll
	do
		if [ $f -nt $d ]
		then
			echo "ABORTING: $f is newer than $d" 1>&2
			foundnewer=1
		fi
	done
done
[ $foundnewer -eq 0 ] || exit 1

zip="/tmp/$name-$version.zip"
if [ ! -z "$zipname" ]
then
	zip="$zipname"
fi

(
	status=0

	kspversion=`cat "$ksphome/readme.txt" | awk 'NR <= 30 && NF == 2 && $1 == "Version" { print $2 }'`
	if [ "$kspversion" = "" ]
	then
		echo "ABORTING: can't find KSP version number" 1>&2
		exit 1
	fi

	if echo '[]' | jq . > /dev/null
	then
		true
	else
		echo "ABORTING: jq not working" 1>&2
		exit 1
	fi

	jsonversion="Resources/$name.version"
	jqfilter='.VERSION | (.MAJOR|tostring) + "." + (.MINOR|tostring) + "." + (.PATCH|tostring) + "." + (.BUILD|tostring)'
	jversion=`jq -r "$jqfilter" "$jsonversion"`
	if [ $? -ne 0 ]
	then
		echo "ABORTING: JSON syntax error in $jsonversion" 1>&2
		exit 1
	fi
	if [ "$version" != "$jversion" ]
	then
		echo "ABORTING: DLL version is $version, JSON version is $jversion" 1>&2
		status=1
	fi

	jqfilter='.KSP_VERSION | (.MAJOR|tostring) + "." + (.MINOR|tostring) + "." + (.PATCH|tostring)'
	jversion=`jq -r "$jqfilter" $jsonversion`
	if [ $? -ne 0 ]
	then
		echo "ABORTING: JSON syntax error in $jsonversion" 1>&2
		exit 1
	fi
	if [ "$kspversion" != "$jversion" ]
	then
		echo "ABORTING: KSP version is $kspversion, JSON version is $jversion" 1>&2
		status=1
	fi

	exit $status
)

if [ $? -ne 0 ]
then
	if [ $force -ne 0 ]
	then
		echo "RESUMING: -f option activated, forcing zip creation" 1>&2
		if [ -z "$zipname" ]
		then
			zip=${zip%.zip}-forced.zip
		fi
	else
		echo "ABORTING: use -f to force zip creation" 1>&2
		exit 1
	fi
fi

tmp=`mktemp -d` || exit 1
trap "rm -rf $tmp" EXIT

dir="$tmp/GameData/$name"
mkdir -p $dir || exit 1

mmglob="ModuleManager.*.dll"
nmmdll=`find "$ksphome/GameData/" -name "$mmglob" -printf . -exec cp {} "$tmp" \; | wc -c`
if [ "$nmmdll" -ne 1 ]
then
	echo "ABORTING: there should be one ModuleManager.*.dll, there are $nmmdll:" 1>&2
	find "$ksphome/GameData/" -name "$mmglob" 1>&2
	exit 1
fi
cp "$tmp"/$mmglob $dir/.. || exit 1
cp ModuleManagerLicense.md $dir/.. || exit 1

cp -r $dll README.md LICENSE.md Resources/* $dir || exit 1

cp $debugdll $dir/${dllname%.dll}.debugdll

rm -f $zip
echo
echo generating release $zip
(
	cd $tmp &&
	zip -vr $zip GameData
) || exit 1
echo generated $zip `du -h $zip | cut -f 1`
echo

