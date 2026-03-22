import React from "react";
import { Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import CustomWrapperPage from "@/app/ui-kit/(wrapper)/_components/CustomWrapperPage";
import ContentOverlayWrapper from "@/app/ui-kit/(wrapper)/_components/ContentOverlayWrapper";
import { IconBriefcase } from "@tabler/icons-react";

const WrapperPage = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Wrapper"
          title="Ui Kits"
          path={["Wrapper"]}
          Icon={IconBriefcase}
        />
        <Row className="overlay-page">
          <CustomWrapperPage />
          <ContentOverlayWrapper />
        </Row>
      </Container>
    </div>
  );
};

export default WrapperPage;
