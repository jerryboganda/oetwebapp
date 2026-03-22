import React from "react";
import { Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import OptionGrid from "@/app/ui-kit/(grid)/_components/OptionGrid";
import ColumGrid from "@/app/ui-kit/(grid)/_components/ColumGrid";
import NestingGrid from "@/app/ui-kit/(grid)/_components/NestingGrid";
import { IconBriefcase } from "@tabler/icons-react";

const GridPage = () => {
  return (
    <>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Grid"
          title="Ui Kits"
          path={["Grid"]}
          Icon={IconBriefcase}
        />
        <Row className="grid-page">
          <OptionGrid />
          <ColumGrid />
          <NestingGrid />
        </Row>
      </Container>
    </>
  );
};

export default GridPage;
