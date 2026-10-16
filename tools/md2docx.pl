#!/usr/bin/perl
#
# Markdown -> .docx (OOXML) converter.
#
#   perl tools/md2docx.pl docs/某方案.md docs/某方案.docx
#
# 为什么自己写：本机没有 pandoc / Node / LibreOffice，注册的 Word.Application
# 实际是 WPS 兼容模式。.docx 本质是一包 XML 的 zip，直接生成比依赖 WPS 自动化可控，
# 中文字体与代码块对齐也能精确指定。只依赖核心 Perl 模块，无需安装任何东西。
#
# 覆盖范围 = docs/*.md 实际用到的 Markdown 子集：
#   标题 / 段落（含软换行合并）/ **粗体** / `行内代码` / 有序与无序列表（3 级嵌套）
#   / 引用 / GFM 管道表格（含单元格内 <br>）/ 围栏代码块 / --- 分隔线
# 不支持：链接、图片、脚注、行内 HTML、缩进式代码块、单星斜体。
# 排版：A4，正文 Microsoft YaHei 10.5pt；代码块 ascii 与 eastAsia 同用 NSimSun，
# 保证中西文宽度 2:1，流程图里的制表符才能对齐。
#
use strict;
use warnings;
use utf8;
use Encode qw(encode);
use IO::Compress::Zip qw(zip $ZipError ZIP_CM_DEFLATE);

my ($src, $dst) = @ARGV;
die "usage: md2docx.pl <in.md> <out.docx>\n" unless $src && $dst;

open(my $fh, '<:encoding(UTF-8)', $src) or die "open $src: $!";
my @lines = <$fh>;
close $fh;
chomp @lines;
s/\r$// for @lines;

# ---------------- text helpers ----------------
sub esc {
    my $s = shift; $s = '' unless defined $s;
    $s =~ s/&/&amp;/g; $s =~ s/</&lt;/g; $s =~ s/>/&gt;/g;
    return $s;
}
sub is_wide {
    my $c = shift; return 0 unless defined $c && length $c;
    return ord($c) >= 0x1100 ? 1 : 0;
}
sub is_wide_punct {
    my $c = shift; return 0 unless defined $c && length $c;
    my $o = ord($c);
    return 1 if $o >= 0x3000 && $o <= 0x303F;      # 。、「」（）
    return 1 if $o >= 0xFF01 && $o <= 0xFF65;      # fullwidth forms
    return 1 if $o == 0x2014 || $o == 0x2026;      # em dash, ellipsis
    return 1 if $o >= 0x2018 && $o <= 0x201D;      # curly quotes
    return 0;
}
# Join soft-wrapped lines. No space when both sides are wide, or either side is
# wide punctuation; otherwise one space (correct CJK/Latin spacing).
sub join_soft {
    my @p = grep { defined } @_;
    my $out = shift @p;
    $out = '' unless defined $out;
    for my $s (@p) {
        next unless defined $s && $s ne '';
        if ($out eq '') { $out = $s; next; }
        my $a = substr($out, -1);
        my $b = substr($s, 0, 1);
        if ((is_wide($a) && is_wide($b)) || is_wide_punct($a) || is_wide_punct($b)) {
            $out .= $s;
        } else {
            $out .= ' ' . $s;
        }
    }
    return $out;
}
sub disp_width {
    my $s = shift; $s = '' unless defined $s;
    $s =~ s/`//g; $s =~ s/\*\*//g;
    my $w = 0;
    $w += (ord($_) >= 0x1100 ? 2 : 1) for split //, $s;
    return $w;
}

# ---------------- inline parsing ----------------
# Single-pass tokenizer: bold may span code spans and vice versa, so nested
# split() on the two delimiters separately does not work.
sub inline_runs {
    my ($text) = @_;
    $text = '' unless defined $text;
    my (@runs, $buf, $bold);
    $buf = ''; $bold = 0;
    my $flush = sub {
        return if $buf eq '';
        push @runs, { text => $buf, ($bold ? (b => 1) : ()) };
        $buf = '';
    };
    my $n = length $text;
    my $p = 0;
    while ($p < $n) {
        my $c = substr($text, $p, 1);
        if ($c eq '`') {
            my $close = index($text, '`', $p + 1);
            if ($close > $p) {
                $flush->();
                my $code = substr($text, $p + 1, $close - $p - 1);
                push @runs, { text => $code, code => 1, ($bold ? (b => 1) : ()) } if length $code;
                $p = $close + 1;
                next;
            }
        } elsif ($c eq '*' && substr($text, $p, 2) eq '**') {
            $flush->();
            $bold = !$bold;
            $p += 2;
            next;
        }
        $buf .= $c;
        $p++;
    }
    $flush->();
    return @runs ? @runs : ({ text => '' });
}
sub cell_runs {
    my ($cell, $bold) = @_;
    my @runs;
    my @segs = split /<br\s*\/?>/i, $cell, -1;      # before XML escaping
    for my $k (0 .. $#segs) {
        push @runs, { br => 1 } if $k > 0;
        push @runs, inline_runs($segs[$k]);
    }
    if ($bold) { for my $r (@runs) { $r->{b} = 1 unless $r->{br} } }
    return @runs;
}
sub split_row {
    my $l = shift;
    $l =~ s/^\s*\|//; $l =~ s/\|\s*$//;
    my @c = split /\|/, $l, -1;
    for (@c) { s/^\s+//; s/\s+$//; }
    return \@c;
}

# ---------------- block parsing ----------------
my @blocks;
my $ord_lists = 0;
my $i = 0;
while ($i <= $#lines) {
    my $line = $lines[$i];

    if ($line =~ /^\s*```/) {                                  # fenced code
        my @code; $i++;
        while ($i <= $#lines && $lines[$i] !~ /^\s*```\s*$/) { push @code, $lines[$i]; $i++ }
        $i++;
        push @blocks, { type => 'code', lines => \@code };
        next;
    }
    if ($line =~ /^\s*$/)               { $i++; next }
    if ($line =~ /^\s*-{3,}\s*$/)       { push @blocks, { type => 'hr' }; $i++; next }
    if ($line =~ /^(#{1,6})\s+(.*)$/)   { push @blocks, { type => 'heading', level => length($1), text => $2 }; $i++; next }

    if ($line =~ /^\s*\|/ && $i < $#lines && $lines[$i+1] =~ /^\s*\|[\s:|-]+\|\s*$/) {   # table
        my @rows = ( split_row($line) );
        $i += 2;
        while ($i <= $#lines && $lines[$i] =~ /^\s*\|/) { push @rows, split_row($lines[$i]); $i++ }
        push @blocks, { type => 'table', rows => \@rows };
        next;
    }
    if ($line =~ /^\s*>\s?(.*)$/) {                            # blockquote
        my @qt = ($1); $i++;
        while ($i <= $#lines && $lines[$i] =~ /^\s*>\s?(.*)$/) { push @qt, $1; $i++ }
        push @blocks, { type => 'quote', text => join_soft(@qt) };
        next;
    }

    my ($is_li, $lvl, $txt, $ordered) = (0, 0, '', 0);
    if ($line =~ /^(\s*)[-*]\s+(.*)$/) {
        $is_li = 1; $lvl = int(length($1) / 2); $lvl = 2 if $lvl > 2; $txt = $2;
    } elsif ($line =~ /^(\s*)(\d+)\.\s+(.*)$/) {
        $is_li = 1; $ordered = 1; $lvl = 0; $txt = $3;
        $ord_lists++ if $2 == 1;
    }

    if ($is_li) {
        my @acc = ($txt); $i++;
        while ($i <= $#lines) {
            my $l = $lines[$i];
            last if $l =~ /^\s*$/;
            last if $l =~ /^\s*[-*]\s+/ || $l =~ /^\s*\d+\.\s+/;
            last if $l =~ /^\s*>/ || $l =~ /^\s*```/ || $l =~ /^\s*\|/;
            last if $l =~ /^#{1,6}\s/ || $l =~ /^\s*-{3,}\s*$/;
            last if $l !~ /^\s+\S/;                            # continuation must be indented
            $l =~ s/^\s+//;
            push @acc, $l; $i++;
        }
        push @blocks, { type => 'li', ordered => $ordered, level => $lvl,
                        text => join_soft(@acc),
                        numid => $ordered ? (1 + $ord_lists) : 1 };
        next;
    }

    my @acc = ($line); $i++;                                   # paragraph
    while ($i <= $#lines) {
        my $l = $lines[$i];
        last if $l =~ /^\s*$/;
        last if $l =~ /^\s*[-*]\s+/ || $l =~ /^\s*\d+\.\s+/;
        last if $l =~ /^\s*>/ || $l =~ /^\s*```/ || $l =~ /^\s*\|/;
        last if $l =~ /^#{1,6}\s/ || $l =~ /^\s*-{3,}\s*$/;
        $l =~ s/^\s+//;
        push @acc, $l; $i++;
    }
    push @blocks, { type => 'para', text => join_soft(@acc) };
}

# ---------------- OOXML emission ----------------
use constant CONTENT_W => 9638;        # A4 (11906) minus 1134 margins each side

sub runs_xml {
    my @runs = @_;
    my $x = '';
    for my $r (@runs) {
        if ($r->{br}) { $x .= '<w:r><w:br/></w:r>'; next }
        my $rpr = '';
        $rpr .= '<w:rStyle w:val="CodeChar"/>' if $r->{code};
        $rpr .= '<w:b/><w:bCs/>'               if $r->{b};
        $rpr = "<w:rPr>$rpr</w:rPr>" if $rpr ne '';
        $x .= '<w:r>' . $rpr . '<w:t xml:space="preserve">' . esc($r->{text}) . '</w:t></w:r>';
    }
    return $x;
}
sub para { my ($style, $extra, @runs) = @_;
    my $ppr = '';
    $ppr .= qq{<w:pStyle w:val="$style"/>} if $style;
    $ppr .= $extra if $extra;
    $ppr = "<w:pPr>$ppr</w:pPr>" if $ppr ne '';
    return '<w:p>' . $ppr . runs_xml(@runs) . '</w:p>';
}

my @HEAD = ('', 'Title', 'Heading1', 'Heading2', 'Heading3', 'Heading3', 'Heading3');
my $doc_title = '';
my $body = '';

for my $b (@blocks) {
    my $t = $b->{type};

    if ($t eq 'heading') {
        $doc_title = $b->{text} if $doc_title eq '' && $b->{level} == 1;
        my $st = $HEAD[$b->{level}] || 'Heading3';
        $body .= para($st, '', inline_runs($b->{text}));
    }
    elsif ($t eq 'para') {
        $body .= para('', '', inline_runs($b->{text}));
    }
    elsif ($t eq 'quote') {
        $body .= para('Quote', '', inline_runs($b->{text}));
    }
    elsif ($t eq 'li') {
        my $np = sprintf('<w:numPr><w:ilvl w:val="%d"/><w:numId w:val="%d"/></w:numPr>',
                         $b->{level}, $b->{numid});
        $body .= para('ListParagraph', $np, inline_runs($b->{text}));
    }
    elsif ($t eq 'hr') {
        $body .= '<w:p><w:pPr><w:pBdr><w:bottom w:val="single" w:sz="6" w:space="1" w:color="BFBFBF"/></w:pBdr>'
               . '<w:spacing w:before="120" w:after="120"/></w:pPr></w:p>';
    }
    elsif ($t eq 'code') {
        my @runs;
        my @cl = @{ $b->{lines} };
        pop @cl while @cl && $cl[-1] =~ /^\s*$/;
        for my $k (0 .. $#cl) {
            push @runs, { br => 1 } if $k > 0;
            push @runs, { text => $cl[$k] };
        }
        @runs = ({ text => '' }) unless @runs;
        $body .= para('CodeBlock', '', @runs);
    }
    elsif ($t eq 'table') {
        my @rows = @{ $b->{rows} };
        my $ncol = 0;
        for my $r (@rows) { $ncol = scalar(@$r) if scalar(@$r) > $ncol }
        next unless $ncol;

        my @w = (0) x $ncol;
        for my $r (@rows) {
            for my $c (0 .. $ncol - 1) {
                my $cell = defined $r->[$c] ? $r->[$c] : '';
                my $mx = 0;
                for my $seg (split /<br\s*\/?>/i, $cell) {
                    my $dw = disp_width($seg); $mx = $dw if $dw > $mx;
                }
                $w[$c] = $mx if $mx > $w[$c];
            }
        }
        for my $c (0 .. $ncol - 1) {
            $w[$c] = 6  if $w[$c] < 6;
            $w[$c] = 44 if $w[$c] > 44;
        }
        my $tot = 0; $tot += $_ for @w;
        my @dxa = map { int(CONTENT_W * $_ / $tot) } @w;
        my $sum = 0; $sum += $_ for @dxa;
        $dxa[-1] += CONTENT_W - $sum;

        my $tbl = '<w:tbl><w:tblPr><w:tblStyle w:val="TableGrid"/>'
                . '<w:tblW w:w="' . CONTENT_W . '" w:type="dxa"/>'
                . '<w:tblLayout w:type="fixed"/></w:tblPr><w:tblGrid>';
        $tbl .= qq{<w:gridCol w:w="$_"/>} for @dxa;
        $tbl .= '</w:tblGrid>';

        for my $ri (0 .. $#rows) {
            my $hdr = ($ri == 0);
            $tbl .= '<w:tr>';
            $tbl .= '<w:trPr><w:tblHeader/><w:cantSplit/></w:trPr>' if $hdr;
            for my $c (0 .. $ncol - 1) {
                my $cell = defined $rows[$ri][$c] ? $rows[$ri][$c] : '';
                my $shd  = $hdr ? '<w:shd w:val="clear" w:color="auto" w:fill="D9D9D9"/>' : '';
                $tbl .= '<w:tc><w:tcPr><w:tcW w:w="' . $dxa[$c] . '" w:type="dxa"/>'
                      . $shd . '<w:vAlign w:val="center"/></w:tcPr>'
                      . para('TableText', '', cell_runs($cell, $hdr))
                      . '</w:tc>';
            }
            $tbl .= '</w:tr>';
        }
        $tbl .= '</w:tbl>';
        $body .= $tbl . '<w:p><w:pPr><w:spacing w:after="0" w:line="120" w:lineRule="auto"/></w:pPr></w:p>';
    }
}

$body .= '<w:sectPr><w:pgSz w:w="11906" w:h="16838"/>'
       . '<w:pgMar w:top="1417" w:right="1134" w:bottom="1417" w:left="1134"'
       . ' w:header="851" w:footer="992" w:gutter="0"/></w:sectPr>';

my $XMLDECL = qq{<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n};
my $WNS = 'xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"';

my $document = $XMLDECL . qq{<w:document $WNS><w:body>$body</w:body></w:document>};

my $styles = $XMLDECL . qq{<w:styles $WNS>} . q{
<w:docDefaults>
 <w:rPrDefault><w:rPr>
  <w:rFonts w:ascii="Microsoft YaHei" w:eastAsia="Microsoft YaHei" w:hAnsi="Microsoft YaHei" w:cs="Microsoft YaHei"/>
  <w:sz w:val="21"/><w:szCs w:val="21"/>
 </w:rPr></w:rPrDefault>
 <w:pPrDefault><w:pPr><w:spacing w:after="120" w:line="300" w:lineRule="auto"/></w:pPr></w:pPrDefault>
</w:docDefaults>
<w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:qFormat/></w:style>
<w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:basedOn w:val="Normal"/><w:qFormat/>
 <w:pPr><w:spacing w:before="0" w:after="260"/><w:outlineLvl w:val="0"/>
  <w:pBdr><w:bottom w:val="single" w:sz="8" w:space="6" w:color="1F3864"/></w:pBdr></w:pPr>
 <w:rPr><w:b/><w:color w:val="1F3864"/><w:sz w:val="40"/><w:szCs w:val="40"/></w:rPr></w:style>
<w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:qFormat/>
 <w:pPr><w:keepNext/><w:spacing w:before="360" w:after="140"/><w:outlineLvl w:val="0"/></w:pPr>
 <w:rPr><w:b/><w:color w:val="1F3864"/><w:sz w:val="30"/><w:szCs w:val="30"/></w:rPr></w:style>
<w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/><w:basedOn w:val="Normal"/><w:qFormat/>
 <w:pPr><w:keepNext/><w:spacing w:before="260" w:after="120"/><w:outlineLvl w:val="1"/></w:pPr>
 <w:rPr><w:b/><w:color w:val="2F5496"/><w:sz w:val="24"/><w:szCs w:val="24"/></w:rPr></w:style>
<w:style w:type="paragraph" w:styleId="Heading3"><w:name w:val="heading 3"/><w:basedOn w:val="Normal"/><w:qFormat/>
 <w:pPr><w:keepNext/><w:spacing w:before="200" w:after="100"/><w:outlineLvl w:val="2"/></w:pPr>
 <w:rPr><w:b/><w:color w:val="595959"/><w:sz w:val="22"/><w:szCs w:val="22"/></w:rPr></w:style>
<w:style w:type="paragraph" w:styleId="Quote"><w:name w:val="Quote"/><w:basedOn w:val="Normal"/><w:qFormat/>
 <w:pPr><w:pBdr><w:left w:val="single" w:sz="18" w:space="8" w:color="A6A6A6"/></w:pBdr>
  <w:ind w:left="284"/><w:spacing w:before="140" w:after="140"/></w:pPr>
 <w:rPr><w:color w:val="404040"/></w:rPr></w:style>
<w:style w:type="paragraph" w:styleId="CodeBlock"><w:name w:val="Code Block"/><w:basedOn w:val="Normal"/>
 <w:pPr><w:shd w:val="clear" w:color="auto" w:fill="F4F4F4"/>
  <w:pBdr>
   <w:top w:val="single" w:sz="4" w:space="4" w:color="D9D9D9"/>
   <w:left w:val="single" w:sz="4" w:space="4" w:color="D9D9D9"/>
   <w:bottom w:val="single" w:sz="4" w:space="4" w:color="D9D9D9"/>
   <w:right w:val="single" w:sz="4" w:space="4" w:color="D9D9D9"/></w:pBdr>
  <w:spacing w:before="140" w:after="140" w:line="240" w:lineRule="auto"/>
  <w:ind w:left="113" w:right="113"/></w:pPr>
 <w:rPr><w:rFonts w:ascii="NSimSun" w:eastAsia="NSimSun" w:hAnsi="NSimSun" w:cs="NSimSun"/>
  <w:sz w:val="18"/><w:szCs w:val="18"/></w:rPr></w:style>
<w:style w:type="paragraph" w:styleId="ListParagraph"><w:name w:val="List Paragraph"/><w:basedOn w:val="Normal"/><w:qFormat/>
 <w:pPr><w:spacing w:after="60"/><w:contextualSpacing/></w:pPr></w:style>
<w:style w:type="paragraph" w:styleId="TableText"><w:name w:val="Table Text"/><w:basedOn w:val="Normal"/>
 <w:pPr><w:spacing w:before="50" w:after="50" w:line="260" w:lineRule="auto"/></w:pPr>
 <w:rPr><w:sz w:val="19"/><w:szCs w:val="19"/></w:rPr></w:style>
<w:style w:type="character" w:styleId="CodeChar"><w:name w:val="Code Char"/>
 <w:rPr><w:rFonts w:ascii="Consolas" w:eastAsia="Microsoft YaHei" w:hAnsi="Consolas" w:cs="Consolas"/>
  <w:color w:val="A31515"/><w:sz w:val="19"/><w:szCs w:val="19"/>
  <w:shd w:val="clear" w:color="auto" w:fill="F2F2F2"/></w:rPr></w:style>
<w:style w:type="table" w:styleId="TableGrid"><w:name w:val="Table Grid"/>
 <w:tblPr><w:tblBorders>
  <w:top w:val="single" w:sz="4" w:space="0" w:color="A6A6A6"/>
  <w:left w:val="single" w:sz="4" w:space="0" w:color="A6A6A6"/>
  <w:bottom w:val="single" w:sz="4" w:space="0" w:color="A6A6A6"/>
  <w:right w:val="single" w:sz="4" w:space="0" w:color="A6A6A6"/>
  <w:insideH w:val="single" w:sz="4" w:space="0" w:color="A6A6A6"/>
  <w:insideV w:val="single" w:sz="4" w:space="0" w:color="A6A6A6"/>
 </w:tblBorders>
 <w:tblCellMar><w:top w:w="60" w:type="dxa"/><w:left w:w="100" w:type="dxa"/>
  <w:bottom w:w="60" w:type="dxa"/><w:right w:w="100" w:type="dxa"/></w:tblCellMar></w:tblPr></w:style>
} . '</w:styles>';

# ---- numbering: one bullet list + one restarting decimal list per ordered list
my $num_lvls = q{
<w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="bullet"/><w:lvlText w:val="&#xF0B7;"/><w:lvlJc w:val="left"/>
 <w:pPr><w:ind w:left="357" w:hanging="357"/></w:pPr>
 <w:rPr><w:rFonts w:ascii="Symbol" w:hAnsi="Symbol" w:hint="default"/></w:rPr></w:lvl>
<w:lvl w:ilvl="1"><w:start w:val="1"/><w:numFmt w:val="bullet"/><w:lvlText w:val="o"/><w:lvlJc w:val="left"/>
 <w:pPr><w:ind w:left="765" w:hanging="357"/></w:pPr>
 <w:rPr><w:rFonts w:ascii="Courier New" w:hAnsi="Courier New" w:hint="default"/></w:rPr></w:lvl>
<w:lvl w:ilvl="2"><w:start w:val="1"/><w:numFmt w:val="bullet"/><w:lvlText w:val="&#xF0A7;"/><w:lvlJc w:val="left"/>
 <w:pPr><w:ind w:left="1173" w:hanging="357"/></w:pPr>
 <w:rPr><w:rFonts w:ascii="Wingdings" w:hAnsi="Wingdings" w:hint="default"/></w:rPr></w:lvl>};
my $dec_lvls = q{
<w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/><w:lvlJc w:val="left"/>
 <w:pPr><w:ind w:left="425" w:hanging="425"/></w:pPr></w:lvl>
<w:lvl w:ilvl="1"><w:start w:val="1"/><w:numFmt w:val="lowerLetter"/><w:lvlText w:val="%2."/><w:lvlJc w:val="left"/>
 <w:pPr><w:ind w:left="850" w:hanging="425"/></w:pPr></w:lvl>
<w:lvl w:ilvl="2"><w:start w:val="1"/><w:numFmt w:val="lowerRoman"/><w:lvlText w:val="%3."/><w:lvlJc w:val="right"/>
 <w:pPr><w:ind w:left="1275" w:hanging="425"/></w:pPr></w:lvl>};

my $numbering = $XMLDECL . qq{<w:numbering $WNS>}
  . qq{<w:abstractNum w:abstractNumId="0"><w:multiLevelType w:val="multilevel"/>$num_lvls</w:abstractNum>}
  . qq{<w:abstractNum w:abstractNumId="1"><w:multiLevelType w:val="multilevel"/>$dec_lvls</w:abstractNum>}
  . q{<w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num>};
for my $n (1 .. $ord_lists) {
    my $id = $n + 1;
    $numbering .= qq{<w:num w:numId="$id"><w:abstractNumId w:val="1"/>}
                . q{<w:lvlOverride w:ilvl="0"><w:startOverride w:val="1"/></w:lvlOverride>}
                . q{<w:lvlOverride w:ilvl="1"><w:startOverride w:val="1"/></w:lvlOverride>}
                . q{<w:lvlOverride w:ilvl="2"><w:startOverride w:val="1"/></w:lvlOverride>}
                . q{</w:num>};
}
$numbering .= '</w:numbering>';

# ---------------- package parts ----------------
my $content_types = $XMLDECL . q{<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
<Default Extension="xml" ContentType="application/xml"/>
<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
<Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
<Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/>
<Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
</Types>};

my $root_rels = $XMLDECL . q{<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>
</Relationships>};

my $doc_rels = $XMLDECL . q{<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/>
</Relationships>};

my @tm = gmtime(time);
my $now = sprintf('%04d-%02d-%02dT%02d:%02d:%02dZ',
                  $tm[5]+1900, $tm[4]+1, $tm[3], $tm[2], $tm[1], $tm[0]);
my $et = esc($doc_title);
my $core = $XMLDECL . qq{<cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
<dc:title>$et</dc:title><dc:creator>Renne</dc:creator><cp:lastModifiedBy>Renne</cp:lastModifiedBy>
<dcterms:created xsi:type="dcterms:W3CDTF">$now</dcterms:created>
<dcterms:modified xsi:type="dcterms:W3CDTF">$now</dcterms:modified>
</cp:coreProperties>};

# ---------------- zip ----------------
my @parts = (
    [ '[Content_Types].xml',          $content_types ],
    [ '_rels/.rels',                  $root_rels     ],
    [ 'word/document.xml',            $document      ],
    [ 'word/styles.xml',              $styles        ],
    [ 'word/numbering.xml',           $numbering     ],
    [ 'word/_rels/document.xml.rels', $doc_rels      ],
    [ 'docProps/core.xml',            $core          ],
);
unlink $dst if -e $dst;
my $zh = IO::Compress::Zip->new($dst,
            Name => $parts[0][0], Zip64 => 0, CanonicalName => 0,
            Method => ZIP_CM_DEFLATE)
    or die "zip open failed: $ZipError\n";
$zh->print(encode('UTF-8', $parts[0][1]));
for my $p (@parts[1 .. $#parts]) {
    $zh->newStream(Name => $p->[0], Zip64 => 0, CanonicalName => 0,
                   Method => ZIP_CM_DEFLATE)
        or die "newStream failed for $p->[0]: $ZipError\n";
    $zh->print(encode('UTF-8', $p->[1]));
}
$zh->close() or die "zip close failed: $ZipError\n";

binmode(STDOUT, ':encoding(UTF-8)');
my %n;
$n{ $_->{type} }++ for @blocks;
printf "%s -> %s\n  title=%s\n  blocks: %s\n  ordered lists: %d\n",
    $src, $dst, $doc_title,
    join(', ', map { "$_=$n{$_}" } sort keys %n), $ord_lists;
