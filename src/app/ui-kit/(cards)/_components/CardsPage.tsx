import React from "react";
import "prismjs/themes/prism.css";
import { Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import BasicCards from "@/app/ui-kit/(cards)/_components/BasicCards";
import CardVariant from "@/app/ui-kit/(cards)/_components/CardVariant";
import CardStyle from "@/app/ui-kit/(cards)/_components/CardStyle";
import CardTab from "@/app/ui-kit/(cards)/_components/CardTab";
import { IconBriefcase } from "@tabler/icons-react";
import PrismCodeWrapper from "@/Component/CommonElements/PrismCodeWrapper";

const CardsPage = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Cards"
          title="Ui Kits"
          path={["Cards"]}
          Icon={IconBriefcase}
        />
        <Row>
          <PrismCodeWrapper>
            <BasicCards />
            <CardVariant />
            <CardStyle />
            <CardTab />
          </PrismCodeWrapper>
        </Row>
      </Container>
    </div>
  );
};

export default CardsPage;
