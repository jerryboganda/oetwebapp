import React from "react";
import { Container } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import "glightbox/dist/css/glightbox.min.css";
import { IconStack2 } from "@tabler/icons-react";
import GalleryLightbox from "./GalleryLightbox";

const GalleryPage = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Gallery"
          title="Apps"
          path={["Gallery"]}
          Icon={IconStack2}
        />
        <GalleryLightbox />
      </Container>
    </div>
  );
};

export default GalleryPage;
