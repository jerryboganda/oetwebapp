import React from "react";
import {
  Card,
  CardBody,
  CardFooter,
  CardHeader,
  Col,
  Container,
  Row,
} from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { IconNews } from "@tabler/icons-react";

const BlankPage = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Blank"
          title=" Other Pages"
          path={["Blank"]}
          Icon={IconNews}
        />
        <Row>
          <Col xs={12}>
            <Card>
              <CardHeader>
                <h5>Default Card</h5>
              </CardHeader>
              <CardBody>
                <h6>Where does it come from ?</h6>
                <p className="text-secondary">
                  Contrary to popular belief, Lorem Ipsum is not simply random
                  text. It has roots in a piece of classical Latin literature
                  from 45 BC, making it over 2000 years old. Richard McClintock,
                  a Latin professor at Hampden-Sydney College in Virginia,
                  looked up one of the more obscure Latin words, consectetur,
                  from a Lorem Ipsum passage, and going through the cites of the
                  word in classical literature, discovered the undoubtable
                  source. Lorem Ipsum comes from sections 1.10.32 and 1.10.33 of
                  &#34;de Finibus Bonorum et Malo rum&#34; (The Extremes of Good
                  and Evil) by Cicero, written in 45 BC. This book is a treatise
                  on the theory of ethics, very popular during the Renaissance.
                  The first line of Lorem Ipsum, &#34;Lorem ipsum dolor sit
                  amet..&#34;, comes from a line in section 1.10.32
                </p>
              </CardBody>
              <CardFooter>
                <p className="float-start text-secondary p-t-10">1 days ago</p>
                <button className="btn btn-primary float-end">Read More</button>
              </CardFooter>
            </Card>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default BlankPage;
