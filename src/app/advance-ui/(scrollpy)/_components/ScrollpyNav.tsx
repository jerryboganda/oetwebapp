import React, { useRef, useState } from "react";
import { Button, Card, CardBody, CardHeader, Col, Container } from "reactstrap";

const ScrollpyNav: React.FC = () => {
  const scrollRef = useRef<HTMLDivElement>(null);
  const [activeId, setActiveId] = useState("scrollspyHeading1");
  const [dropdownOpen, setDropdownOpen] = useState(false);

  const handleScroll = () => {
    if (!scrollRef.current) return;
    const scrollTop = scrollRef.current.scrollTop;
    const children = scrollRef.current.children;

    for (let i = 0; i < children.length; i++) {
      const child = children[i] as HTMLElement;
      const offsetTop = child.offsetTop;
      const offsetHeight = child.offsetHeight;

      if (
        scrollTop + 50 >= offsetTop &&
        scrollTop + 50 < offsetTop + offsetHeight
      ) {
        setActiveId(child.id);
        break;
      }
    }
  };

  const handleClick = (id: string) => {
    setActiveId(id);
    const target = document.getElementById(id);
    target?.scrollIntoView({ behavior: "smooth" });
    setDropdownOpen(false);
  };

  return (
    <Col xs="12">
      <Card>
        <CardHeader>
          <h5>Scrollpy in Navbar</h5>
        </CardHeader>
        <CardBody>
          <div
            id="navbar-example2"
            className="navbar navbar-expand-lg scrollpy-navbar bg-body-tertiary"
          >
            <Container fluid>
              <a className="navbar-brand" href="#">
                <img
                  src="/images/logo/polytronx-dark.svg"
                  className="w-150"
                  alt="#"
                />
              </a>
              <Button
                className="navbar-toggler"
                onClick={() => setDropdownOpen(!dropdownOpen)}
              >
                <span className="navbar-toggler-icon"></span>
              </Button>
              <div
                className={`collapse navbar-collapse ${dropdownOpen ? "show" : ""}`}
              >
                <ul className="nav nav-pills ms-auto">
                  {[
                    { id: "scrollspyHeading1", label: "First" },
                    { id: "scrollspyHeading2", label: "Second" },
                  ].map((link) => (
                    <li key={link.id} className="nav-item">
                      <button
                        className={`nav-link nav-pill-primary ${activeId === link.id ? "active" : ""}`}
                        onClick={() => handleClick(link.id)}
                      >
                        {link.label}
                      </button>
                    </li>
                  ))}
                  <li className="nav-item dropdown">
                    <button
                      className={`nav-link dropdown-toggle nav-pill-primary ${dropdownOpen ? "show" : ""}`}
                      onClick={() => setDropdownOpen(!dropdownOpen)}
                    >
                      Dropdown
                    </button>
                    <ul
                      className={`dropdown-menu rounded-1 ${dropdownOpen ? "show" : ""}`}
                    >
                      {[
                        { id: "scrollspyHeading3", label: "Third" },
                        { id: "scrollspyHeading4", label: "Fourth" },
                        { id: "scrollspyHeading5", label: "Fifth" },
                      ].map((link) => (
                        <li key={link.id}>
                          <button
                            className="dropdown-item nav-pill-primary"
                            onClick={() => handleClick(link.id)}
                          >
                            {link.label}
                          </button>
                        </li>
                      ))}
                      <li>
                        <hr className="dropdown-divider" />
                      </li>
                    </ul>
                  </li>
                </ul>
              </div>
            </Container>
          </div>
          <div
            ref={scrollRef}
            onScroll={handleScroll}
            className="scrollspy-example p-3 rounded-2 h-250 overflow-y-scroll app-scroll"
          >
            <h5 className="f-w-500 mb-2 text-dark" id="scrollspyHeading1">
              First paragraph
            </h5>
            <p className="f-s-15 text-secondary mg-b-14">
              Platea platea, sapien rutrum duis adipiscing, dictumst. Arcu nibh.
              Ligula tellus senectus, penatibus maecenas laoreet. Purus odio
              sociis phasellus habitasse nulla ligula duis interdum, ultrices
              aliquam. Convallis odio augue pellentesque inceptos varius
              fermentum facilisi vel eget porttitor neque, congue suscipit
              conubia justo nibh. Semper sollicitudin nibh volutpat class. Nisl
              congue.
            </p>

            <h5 className="f-w-500 mb-2 text-dark" id="scrollspyHeading2">
              Second paragraph
            </h5>
            <p className="f-s-15 text-secondary mg-b-14">
              Lectus torquent sapien placerat bibendum, convallis cras ut nunc
              senectus ultricies venenatis, sapien. Pellentesque condimentum.
              Nisl. Nisl primis est rhoncus. Purus massa purus urna, fermentum
              nec auctor eu ultricies hac. Auctor curabitur dolor faucibus.
              Sagittis. Fringilla eleifend. Eu mi nam montes odio habitasse mus
              pede hendrerit. Massa malesuada sit arcu aenean taciti montes
              etiam facilisi aptent, quisque commodo cubilia nascetur habitasse
              primis elit ridiculus lectus mus cum sem nibh vivamus.
            </p>

            <h5 className="f-w-500 mb-2 text-dark" id="scrollspyHeading3">
              Third paragraph
            </h5>
            <p className="f-s-15 text-secondary mg-b-14">
              Ligula platea at eleifend vivamus nibh porta auctor ornare
              pellentesque cras et donec varius quam tempus. Mattis.
              Sollicitudin diam quisque libero mattis phasellus dui placerat
              mauris, hymenaeos aliquet fermentum facilisis turpis rhoncus
              nascetur fusce, tempus ligula mus senectus sociosqu proin donec
              quis nibh augue etiam quis nunc accumsan dui placerat imperdiet
              natoque. Erat potenti arcu euismod scelerisque nisi. Netus eget
              hendrerit facilisis donec risus. Nam fusce lobortis mi leo diam.
            </p>

            <h5 className="f-w-500 mb-2 text-dark" id="scrollspyHeading4">
              Fourth paragraph
            </h5>
            <p className="f-s-15 text-secondary mg-b-14">
              Diam condimentum etiam. In adipiscing dis aliquet nam ipsum etiam
              per viverra Quam platea posuere quis nunc et, vitae congue natoque
              lobortis laoreet. Sapien potenti augue litora porta mi vitae
              conubia natoque justo auctor pretium et convallis habitant potenti
              sed ridiculus velit mattis quam sociosqu venenatis fames vitae
              parturient nisl pretium pulvinar eros ultricies massa feugiat
              sapien sagittis luctus ultrices leo conubia auctor. Lorem sed
              facilisi donec mollis facilisi. Pulvinar.
            </p>

            <h5 className="f-w-500 mb-2 text-dark" id="scrollspyHeading5">
              Fifth paragraph
            </h5>
            <p className="f-s-15 text-secondary">
              Hymenaeos tincidunt donec vivamus suspendisse condimentum
              tincidunt vestibulum varius enim, odio gravida pellentesque fames
              Ac orci bibendum nullam eu posuere natoque tempus blandit lobortis
              tortor hymenaeos faucibus eleifend faucibus ultrices etiam etiam
              luctus, volutpat nostra nunc Est sit sodales ad malesuada justo
              dignissim eget est cum accumsan maecenas tempus orci ipsum a nisl
              vel porta. Suspendisse gravida placerat vel cursus facilisi
              parturient justo diam pede conubia vulputate vivamus libero
              iaculis primis sociosqu mattis non natoque penatibus adipiscing
              mollis fermentum ac ut feugiat pulvinar Lobortis nibh amet.
              Adipiscing nec phasellus primis. Pretium urna phasellus mi
              habitant tellus a ac ornare posuere.
            </p>
          </div>
        </CardBody>
      </Card>
    </Col>
  );
};
export default ScrollpyNav;
