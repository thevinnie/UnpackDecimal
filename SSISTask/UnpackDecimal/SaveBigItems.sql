USE [tempdb]
GO
/****** Object:  Table [dbo].[longpacked]    Script Date: 11/09/2005 15:54:37 ******/
IF  EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[longpacked]') AND type in (N'U'))
DROP TABLE [dbo].[longpacked]

go

create table longpacked
(
idcol int,
pack13 binary(13),
pack14 binary(14),
pack15 binary(15)
)

INSERT INTO [tempdb].[dbo].[longpacked]
           ([idcol]
           ,[pack13]
           ,[pack14]
           ,[pack15])
     VALUES
           (1, 0x9999999999999999999999999c, 0x999999999999999999999999999c, 0x99999999999999999999999999999c)
INSERT INTO [tempdb].[dbo].[longpacked]
           ([idcol]
           ,[pack13]
           ,[pack14]
           ,[pack15])
     VALUES
           (1, 0x0999999999999999999999999c, 0x099999999999999999999999999c, 0x09999999999999999999999999999c)
