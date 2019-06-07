use master
go
if DB_ID (N'BTIG_sample') is not null
drop database BTIG_sample
go
create database BTIG_sample
go
use BTIG_sample
go
create table Processed
(
	filePath nvarchar(4000),
	updatedText nvarchar(max),
	startTime DateTime,
	endTime DateTime default getdate()
)
go
create table CharCount
(
	filePath nvarchar(4000),
	charValue nchar(1),
	charCount int,
	updatedAt DateTime default getdate()
)
go
create table WordCount
(
	filePath nvarchar(4000),
	word nvarchar(max),
	wordCount int,
	updatedAt DateTime default getdate()
)
go
/*
select * from Processed
select * from CharCount order by charValue
select * from WordCount
*/

create proc uspGetTopWords @maxWords int
as
-- exec uspGetTopWords 50
with topWords as (select filePath, word, wordCount,
ROW_NUMBER() over (partition by filePath order by filePath asc, wordCount desc, word asc) as rowNum
from BTIG_sample..WordCount)
select filePath, word, wordCount
from topWords
where rowNum <= @maxWords
order by filePath asc, wordCount desc, word asc
go

create proc uspGetRollingCharCount @ceiling int
as
-- exec uspGetRollingCharCount 200000
with topChars as (select filePath, charValue, charCount,
ROW_NUMBER() over (partition by filePath order by filePath asc, charCount desc, charValue asc) as ranking
from BTIG_sample..CharCount)
select tc1.filePath, tc1.charValue, --tc1.charCount,
sum(tc2.charCount) as runningTotal, tc1.ranking
from topChars tc1
inner join topChars tc2 on tc2.filePath = tc1.filePath and tc2.ranking <= tc1.ranking
group by tc1.filePath, tc1.charValue, tc1.charCount, tc1.ranking
having sum(tc2.charCount) <= @ceiling
order by tc1.filePath asc, tc1.ranking asc
go

create proc uspGetCharCountByWords
as
select wc.filePath, wc.word, sum(cc.charCount) as charCount
from BTIG_sample..WordCount wc
inner join BTIG_sample..CharCount cc on cc.filePath = wc.filePath and charindex(cast(cc.charValue as varchar), wc.word) > 0
group by wc.filePath, wc.word
order by wc.filePath, wc.word
go
